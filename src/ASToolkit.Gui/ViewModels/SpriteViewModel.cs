using System.Collections.ObjectModel;
using System.Diagnostics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
using ASToolkit.Gui.Core;
using ASToolkit.Gui.Models;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Gui.ViewModels;

/// <summary>
/// Represents the animation player of the selected object.
/// </summary>
public partial class SpriteViewModel : ViewModelBase
{
    private const int PreviewSize = 300;
    private static readonly Rgb24 Backdrop = new(48, 48, 48);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(30);

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private MapRenderer? _renderer;
    private GameObject _object;
    private VidResource? _sprite;
    private KnownObject _id;
    private int _step;

    public SpriteViewModel()
    {
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();
    }

    public ObservableCollection<AnimationState> States { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "click on an object to inspect it";

    [ObservableProperty]
    public partial Bitmap? Frame { get; set; }

    [ObservableProperty]
    public partial double FrameWidth { get; set; }

    [ObservableProperty]
    public partial double FrameHeight { get; set; }

    [ObservableProperty]
    public partial AnimationState? SelectedState { get; set; }

    [ObservableProperty]
    public partial int Direction { get; set; }

    [ObservableProperty]
    public partial int MaxDirection { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; } = true;

    [ObservableProperty]
    public partial string Info { get; set; } = "";

    /// <summary>
    /// Shows an object of a scene, starting on its first state in the direction it faces.
    /// </summary>
    public void Show(MapRenderer renderer, ObjectTable objects, SceneObject source)
    {
        if (!objects.TryFind(source.Id, out var obj))
        {
            return;
        }

        _renderer = renderer;
        _object = obj;
        _id = source.Id;
        _sprite = renderer.GetSprite(source.Id);
        string sprite = _sprite.HasValue
            ? $"{_sprite.Value.Flags}  {_sprite.Value.FrameCount} frames  {_sprite.Value.Width}x{_sprite.Value.Height}  {_sprite.Value.Speed} ms"
            : "missing";
        Title = $"{(int)source.Id}  {objects.GetName(source.Id)}\n{objects.GetPath(source.Id)}  type {obj.Type}  flags 0x{(uint)obj.Flags:x}\nsprite {sprite}";
        States.Clear();
        for (int state = 0; state < StateValues.Count; state++)
        {
            if (obj.FrameCounts[state] > 0)
            {
                States.Add(new AnimationState(state, obj.FrameCounts[state]));
            }
        }

        MaxDirection = Math.Max(0, obj.Directions - 1);
        Direction = obj.GetDirection(source.Facing);
        SelectedState = States.FirstOrDefault();
        Restart();
        _timer.Start();
    }

    partial void OnSelectedStateChanged(AnimationState? value) => Restart();

    partial void OnDirectionChanged(int value) => Restart();

    private void Restart()
    {
        _step = 0;
        _clock.Restart();
        ShowFrame();
    }

    private void Tick()
    {
        if (!IsPlaying || SelectedState is null || _sprite is null)
        {
            return;
        }

        int duration = Math.Max(1, _object.GetFrameDuration(SelectedState.State, _sprite.Value.Speed));
        if (_clock.ElapsedMilliseconds >= duration)
        {
            _clock.Restart();
            _step++;
            ShowFrame();
        }
    }

    private void ShowFrame()
    {
        var state = SelectedState;
        if (_renderer is null || state is null)
        {
            ClearFrame();
            Info = "";
            return;
        }

        if (_sprite is null)
        {
            ClearFrame();
            Info = "The sprite file is missing";
            return;
        }

        var sprite = _sprite.Value;

        int frame = _object.GetStateStart(state.State) + Direction * state.FrameCount + _step % state.FrameCount;
        if (frame >= sprite.FrameCount)
        {
            ClearFrame();
            Info = $"Frame {frame} is out of range";
            return;
        }

        using var image = _renderer.DrawFrame(_id, frame, Backdrop);
        if (image is null)
        {
            ClearFrame();
            Info = $"Frame {frame} cannot be decoded";
            return;
        }

        SetFrame(BitmapConversion.ToBitmap(image), image.Width, image.Height);
        Info = sprite.Flags.HasFlag(VidFlags.OmniLight) ? $"Light frame {frame} of {sprite.FrameCount}, color #{sprite.GetLightColor(frame):x6}"
            : $"Frame {frame} of {sprite.FrameCount}";
    }

    private void ClearFrame()
    {
        var previous = Frame;
        Frame = null;
        previous?.Dispose();
    }

    private void SetFrame(Bitmap bitmap, int width, int height)
    {
        double factor = width > PreviewSize || height > PreviewSize ? Math.Min(PreviewSize / (double)width, PreviewSize / (double)height)
            : Math.Max(1, Math.Min(PreviewSize / width, PreviewSize / height));
        var previous = Frame;
        Frame = bitmap;
        FrameWidth = width * factor;
        FrameHeight = height * factor;
        previous?.Dispose();
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Numerics;
using ASToolkit.Bot.Rendering;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Gui.Core;
using ASToolkit.Gui.Models;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Gui.ViewModels;

/// <summary>
/// Represents the inspector of a sprite picked on the map, which plays the frames of its states as the engine draws them.
/// </summary>
public partial class SpriteViewModel : ViewModelBase
{
    private const int PreviewSize = 300;
    private const uint Backdrop = 0xff303030;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(30);

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private Map? _map;
    private Sprite? _sprite;
    private GameObject _record;
    private int _step;
    private bool _isStale;

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
    public partial Avalonia.Size FrameSize { get; set; }

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
    /// Shows a sprite of a map.
    /// </summary>
    /// <param name="map">The map, which is locked while its sprite is drawn.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="sprite">The sprite.</param>
    public void Show(Map map, ObjectTable objects, Sprite sprite)
    {
        var vid = sprite.Vid;
        bool hasRecord = objects.TryFind(vid.RecordId, out _record);
        ref readonly var obj = ref _record;
        _map = map;
        _sprite = sprite;
        Title = $"{(int)vid.Id}  {objects.GetName(vid.Id)}\n{objects.GetPath(vid.Id)}  class {obj.SpriteClass}  flags 0x{(int)obj.Properties:x}\n" +
                $"{vid.FileFrameCount} frames  {vid.FrameSize.Width}x{vid.FrameSize.Height}  {vid.SpriteSpeed} ms";
        States.Clear();
        if (!hasRecord)
        {
            ClearFrame();
            return;
        }

        for (int state = 0; state < GameObject.StateCount; state++)
        {
            if (obj.FrameCounts[state] > 0)
            {
                States.Add(new AnimationState((SpriteAnimation)state, obj.FrameCounts[state]));
            }
        }

        MaxDirection = Math.Max(0, obj.Directions - 1);
        Direction = sprite.Direction.GetDirectionIndex(obj.Directions);
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
        if (_isStale)
        {
            ShowFrame();
        }

        if (!IsPlaying || SelectedState is null || _sprite is null)
        {
            return;
        }

        int duration = Math.Max(1, _record.GetFrameDuration(SelectedState.State, _sprite.Vid.SpriteSpeed));
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
        var map = _map;
        var sprite = _sprite;
        if (map is null || sprite is null || state is null)
        {
            ClearFrame();
            Info = "";
            return;
        }

        var vid = sprite.Vid;
        int frame = _record.GetStateStart(state.State) + Direction * state.FrameCount + _step % state.FrameCount;
        if (frame >= vid.FileFrameCount)
        {
            ClearFrame();
            Info = $"Frame {frame} is out of range";
            return;
        }

        _isStale = !Monitor.TryEnter(map);
        if (_isStale)
            return;

        var size = new System.Drawing.Size(Math.Max(1, vid.FrameSize.Width * 2), Math.Max(1, vid.FrameSize.Height * 2));
        var screen = new Screen(size, true);
        var pixels = new Bgra32[size.Width * size.Height];
        var savedShift = map.Shift;
        try
        {
            int saved = sprite.Frame;
            var center = sprite.Position.ToDrawPosition();
            Graphics.ClearScreen(screen, Backdrop);
            sprite.Frame = frame;
            map.Shift = center - new Vector2(size.Width, size.Height) * 0.5f;
            vid.Draw(screen, sprite);
            sprite.Frame = saved;
        }
        finally
        {
            map.Shift = savedShift;
            Monitor.Exit(map);
        }

        screen.CopyTo(pixels);
        using var image = Image.LoadPixelData(MapImage.PngConfiguration, pixels, size.Width, size.Height);
        using var opaque = image.CloneAs<Rgb24>();
        SetFrame(BitmapConversion.ToBitmap(opaque), size);
        Info = $"Frame {frame} of {vid.FileFrameCount}";
    }

    private void ClearFrame()
    {
        var previous = Frame;
        Frame = null;
        previous?.Dispose();
    }

    private void SetFrame(Bitmap bitmap, System.Drawing.Size size)
    {
        double factor = size.Width > PreviewSize || size.Height > PreviewSize ? Math.Min(PreviewSize / (double)size.Width, PreviewSize / (double)size.Height)
            : Math.Max(1, Math.Min(PreviewSize / size.Width, PreviewSize / size.Height));
        var previous = Frame;
        Frame = bitmap;
        FrameSize = new Avalonia.Size(size.Width * factor, size.Height * factor);
        previous?.Dispose();
    }
}

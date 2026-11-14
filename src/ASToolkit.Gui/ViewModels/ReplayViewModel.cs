using System.Drawing;
using System.Diagnostics;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Simulation;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Gui.ViewModels;

/// <summary>
/// Represents the live playback of a playthrough simulation of the map open in the viewer, seen through the game's own camera.
/// </summary>
public partial class ReplayViewModel : ViewModelBase
{
    private static readonly Size DefaultViewport = new(1280, 720);

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private ReplayPlayback? _playback;
    private Bgra32[] _pixels = [];
    private CursorImage _cursor;
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private int _time;
    private int _baseTime;
    private bool _seeking;
    private int _loadVersion;

    public ReplayViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>
    /// Gets or sets the viewer whose open game and map are simulated.
    /// </summary>
    public MapViewerViewModel? Source { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a map in the viewer, then run a simulation here";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsFollowing { get; set; } = true;

    [ObservableProperty]
    public partial int FrameIndex { get; set; }

    [ObservableProperty]
    public partial int FrameCount { get; set; }

    [ObservableProperty]
    public partial double Speed { get; set; } = 1;

    [ObservableProperty]
    public partial double Zoom { get; set; } = 1;

    [ObservableProperty]
    public partial Bitmap? Frame { get; set; }

    /// <summary>
    /// Gets or sets the size of the view in device pixels, filled edge to edge.
    /// </summary>
    public Size Viewport { get; set; } = DefaultViewport;

    /// <summary>
    /// Gets or sets the view's offset from the game's camera in map pixels, or null while the view follows the game's camera.
    /// </summary>
    public Vector2? CameraOffset { get; set; }

    [RelayCommand]
    public async Task RunAsync()
    {
        var source = Source;
        string? game = source?.GameFolder;
        string? map = source?.SelectedMap;
        var objects = source?.Objects;
        var renderer = source?.Renderer;
        if (game is null || map is null || objects is null || renderer is null)
        {
            Status = "Open a map in the viewer first";
            return;
        }

        string? cursorPath = GamePath.Resolve(game, Path.Combine("cursores", "attack.ani"));
        _cursor = cursorPath is null ? default : new CursorImage(File.ReadAllBytes(cursorPath));

        int version = ++_loadVersion;
        IsRunning = true;
        IsPlaying = false;
        Status = $"Simulating {map}...";
        try
        {
            var playback = await Task.Run(() => ReplayPlayback.Run(game, map, objects, renderer));
            if (version != _loadVersion)
            {
                return;
            }

            _playback = playback;
            var result = playback.Result;
            FrameCount = playback.FrameCount;
            FrameIndex = 0;
            _time = 0;
            _baseTime = 0;
            Zoom = Math.Max(Viewport.Width / (float)DefaultViewport.Width, Viewport.Height / (float)DefaultViewport.Height);
            CameraOffset = null;
            IsFollowing = true;
            IsReady = true;
            IsPlaying = playback.FrameCount > 0;
            _clock.Restart();
            Status = $"{(result.Survived ? "survived" : "died")}  progress {result.Progress:P0}  kills {result.Kills}  {playback.FrameCount} frames";
            DrawFrame();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            if (version == _loadVersion)
            {
                Status = $"Simulation failed: {exception.Message}";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsRunning = false;
            }
        }
    }

    partial void OnFrameIndexChanged(int value)
    {
        if (_seeking)
        {
            return;
        }

        var playback = _playback;
        if (playback is not null && playback.FrameCount > 0)
        {
            _time = playback.GetFrameTime(value);
            _baseTime = _time;
            _clock.Restart();
        }

        DrawFrame();
    }

    partial void OnZoomChanged(double value) => DrawFrame();

    partial void OnSpeedChanged(double value)
    {
        _baseTime = _time;
        _clock.Restart();
    }

    /// <summary>
    /// Sets the view's offset from the game's camera by a screen drag, in device pixels, and stops it from following the camera.
    /// </summary>
    public void Pan(Vector2 screenDelta)
    {
        float scale = (float)Math.Min(1, Zoom);
        var offset = CameraOffset ?? Vector2.Zero;
        CameraOffset = offset - screenDelta / scale;
        IsFollowing = false;
        DrawFrame();
    }

    partial void OnIsFollowingChanged(bool value)
    {
        if (value)
        {
            CameraOffset = null;
            DrawFrame();
        }
    }

    private void Tick()
    {
        var playback = _playback;
        if (!IsPlaying || playback is null || playback.FrameCount == 0)
        {
            return;
        }

        _time = Math.Min(playback.GetFrameTime(playback.FrameCount - 1), _baseTime + (int)(_clock.ElapsedMilliseconds * Speed));
        int index = FrameIndex;
        while (index < playback.FrameCount - 1 && playback.GetFrameTime(index + 1) <= _time)
        {
            index++;
        }

        if (index != FrameIndex)
        {
            _seeking = true;
            FrameIndex = index;
            _seeking = false;
        }

        DrawFrame();
        if (index >= playback.FrameCount - 1)
        {
            _time = playback.GetFrameTime(index);
            IsPlaying = false;
        }
    }

    private void DrawFrame()
    {
        var playback = _playback;
        if (playback is null || playback.FrameCount == 0)
        {
            return;
        }

        float renderScale = (float)Math.Min(1, Zoom);
        var buffer = Zoom <= 1 ? Viewport : new Size((int)(Viewport.Width / Zoom), (int)(Viewport.Height / Zoom));
        if (_pixels.Length != buffer.Width * buffer.Height)
        {
            _pixels = new Bgra32[buffer.Width * buffer.Height];
        }

        playback.Render(_pixels, buffer, renderScale, _time, CameraOffset.GetValueOrDefault(), _cursor);
        Frame = Upload(buffer);
    }

    // Alternates between two bitmaps to give the bound Frame property a new reference without allocating
    private unsafe WriteableBitmap Upload(Size size)
    {
        var bitmap = _back;
        if (bitmap is null || bitmap.PixelSize.Width != size.Width || bitmap.PixelSize.Height != size.Height)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new Avalonia.PixelSize(size.Width, size.Height), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        using (var buffer = bitmap.Lock())
        {
            for (int row = 0; row < size.Height; row++)
            {
                _pixels.AsSpan(row * size.Width, size.Width).CopyTo(new Span<Bgra32>((byte*)buffer.Address + row * buffer.RowBytes, size.Width));
            }
        }

        _back = _front;
        _front = bitmap;
        return bitmap;
    }
}

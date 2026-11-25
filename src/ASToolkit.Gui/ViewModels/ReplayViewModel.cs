using System.Drawing;
using System.Diagnostics;
using ASToolkit.Audio;
using ASToolkit.Core;
using ASToolkit.Bot;
using ASToolkit.Gui.Core;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Gui.ViewModels;

/// <summary>
/// Represents the live playback of the bot's run through the map open in the viewer, seen through the game's camera.
/// </summary>
public sealed partial class ReplayViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan SlowRender = TimeSpan.FromMilliseconds(100);
    private readonly Stopwatch _clock = new();

    private ReplayPlayback? _playback;
    private Bgra32[] _pixels = [];
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private int _time;
    private int _baseTime;
    private bool _seeking;
    private int _loadVersion;
    private bool _isRendering;
    private bool _isDrawPending;
    private const int Inaudible = -1;

    private SoundOutput? _sound;
    private volatile int _audibleGeneration = Inaudible;
    private int _generation;
    private bool _isResyncDue;
    private bool _isShown;

    /// <summary>
    /// Gets or sets the viewer whose open game and map the bot plays.
    /// </summary>
    public MapViewerViewModel? Source { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Open a map in the viewer, then run the bot here";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial int FrameIndex { get; set; }

    [ObservableProperty]
    public partial int FrameCount { get; set; }

    [ObservableProperty]
    public partial float Speed { get; set; } = 1;

    [ObservableProperty]
    public partial Bitmap? Frame { get; set; }

    [RelayCommand]
    public async Task RunAsync()
    {
        var source = Source;
        string? game = source?.GameFolder;
        string? map = source?.SelectedMap;
        var objects = source?.Objects;
        if (game is null || map is null || objects is null)
        {
            Status = "Open a map in the viewer first";
            return;
        }

        int version = ++_loadVersion;
        IsRunning = true;
        IsPlaying = false;
        Status = $"Playing {map}...";
        try
        {
            var playback = await Task.Run(() => ReplayPlayback.Run(game, map, objects));
            if (version != _loadVersion)
            {
                return;
            }

            _playback = playback;
            _sound?.Dispose();
            _sound = new SoundOutput(game, objects.Sounds);
            BeginResync();
            var result = playback.Result;
            FrameCount = playback.FrameCount;
            FrameIndex = 0;
            _time = 0;
            _baseTime = 0;
            IsReady = true;
            IsPlaying = playback.FrameCount > 0;
            _clock.Restart();
            Status = $"{(result.Survived ? "survived" : "died")}  progress {result.Progress:P0}  kills {result.Kills}  {playback.FrameCount} frames";
            DrawFrame();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            if (version == _loadVersion)
            {
                Status = $"Could not play the map: {ex.Message}";
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

        BeginResync();
        DrawFrame();
    }

    partial void OnSpeedChanged(float value)
    {
        _baseTime = _time;
        _clock.Restart();
        BeginResync();
    }

    partial void OnIsPlayingChanged(bool value) => BeginResync();

    /// <summary>
    /// Shows the frame for the playback clock's current time.
    /// </summary>
    public void Tick()
    {
        _isShown = true;
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

    private bool IsAtNormalSpeed => Speed.IsExactly(1);

    private void DrawFrame() => _ = DrawFrameAsync();

    /// <summary>
    /// Stops the sound until the playback is shown again.
    /// </summary>
    public void SilenceSound()
    {
        _isShown = false;
        BeginResync();
    }

    /// <summary>
    /// Closes the audio device.
    /// </summary>
    public void Dispose()
    {
        _audibleGeneration = Inaudible;
        _sound?.Dispose();
        _sound = null;
    }

    private void BeginResync()
    {
        _audibleGeneration = Inaudible;
        _isResyncDue = true;

        if (_sound is not null)
        {
            _generation = _sound.Silence();
        }
    }


    private async Task DrawFrameAsync()
    {
        var playback = _playback;
        if (playback is null || playback.FrameCount == 0)
        {
            return;
        }

        if (_isRendering)
        {
            _isDrawPending = true;
            return;
        }

        var buffer = playback.FrameSize;

        if (_pixels.Length != buffer.Width * buffer.Height)
        {
            _pixels = new Bgra32[buffer.Width * buffer.Height];
        }

        var pixels = _pixels;
        int time = _time;
        var sound = _sound;
        int generation = _generation;
        bool isResyncing = _isResyncDue && _isShown && IsPlaying && IsAtNormalSpeed;
        _isRendering = true;
        bool isWritten;
        long started = Stopwatch.GetTimestamp();
        try
        {
            isWritten = await Task.Run(() =>
            {
                while (playback.TryStepToward(time, out bool isFull))
                {
                    int audible = _audibleGeneration;

                    if (isFull && audible != Inaudible)
                    {
                        sound?.Sync(playback.Sound, playback.RealTime, false, audible);
                    }
                }

                bool written = playback.Render(pixels);

                if (isResyncing)
                {
                    sound?.Sync(playback.Sound, playback.RealTime, true, generation);
                }

                return written;
            });
        }
        finally
        {
            _isRendering = false;
        }

        if (Stopwatch.GetElapsedTime(started) > SlowRender && playback == _playback)
        {
            _time = time;
            _baseTime = time;
            _clock.Restart();
        }

        if (isWritten && playback == _playback)
        {
            Frame = Upload(buffer);
        }

        if (isResyncing && generation == _generation && sound == _sound && playback == _playback)
        {
            _isResyncDue = false;
            _audibleGeneration = generation;
        }

        if (_isDrawPending)
        {
            _isDrawPending = false;
            await DrawFrameAsync();
        }
    }

    private WriteableBitmap Upload(Size size)
    {
        var bitmap = _back;
        if (bitmap is null || bitmap.PixelSize.Width != size.Width || bitmap.PixelSize.Height != size.Height)
        {
            bitmap?.Dispose();
            bitmap = BitmapConversion.CreateOpaque(new Avalonia.PixelSize(size.Width, size.Height));
        }

        BitmapConversion.Write(bitmap, _pixels);
        _back = _front;
        _front = bitmap;
        return bitmap;
    }
}

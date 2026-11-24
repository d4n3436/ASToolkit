using System.Drawing;
using System.Numerics;
using ASToolkit.Analysis.Reference;
using ASToolkit.Analysis.Routes;
using ASToolkit.Analysis.Scripting;
using ASToolkit.Bot.Rendering;
using ASToolkit.Engine;
using ASToolkit.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Bot;

/// <summary>
/// Provides the playback of a recorded playthrough of one level, with the frames drawn as it replays.
/// </summary>
public sealed class ReplayPlayback
{
    private const int FrameInterval = 100;
    private const int DrawnLead = 1100;

    private readonly Playthrough _play;
    private readonly RouteAnalysis _analysis;
    private readonly PlayerLoadout _loadout;
    private readonly DemoResource _demo;
    private readonly int[] _frameTimes;
    private readonly FramePacer _pacer;
    private Map _map;
    private uint _now;
    private bool _isStepped;
    private int _shownTime;
    private Bgra32[]? _rendered;
    private int _renderedTime;

    private ReplayPlayback(Playthrough play, RouteAnalysis analysis, PlaythroughResult result)
    {
        _play = play;
        _pacer = new FramePacer(play.FrameTimes);
        _analysis = analysis;
        Result = result;
        _loadout = result.StartingLoadout ?? throw new ArgumentException("The playthrough does not include a starting loadout.", nameof(result));
        _demo = result.Demo ?? throw new ArgumentException("The playthrough does not include a recorded demo.", nameof(result));

        _map = Load();
        int[] times = _map.EnumerateDemoFrameTimes().ToArray();

        _frameTimes = times
            .DistinctBy(time => time / FrameInterval)
            .Append(times.Length > 0 ? times[^1] : 0)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Gets the outcome of the playthrough, including its demo.
    /// </summary>
    public PlaythroughResult Result { get; }

    /// <summary>
    /// Gets the number of steps of the playthrough, one for every 100 ms of play and one for its last frame.
    /// </summary>
    public int FrameCount => _frameTimes.Length;

    /// <summary>
    /// Gets the size of the frames, equal to the screen size.
    /// </summary>
    public Size FrameSize => _map.Session.Graphics.Screen!.Size;

    /// <summary>
    /// Gets the sound state of the map.
    /// </summary>
    public Sound Sound => _map.Sound;

    /// <summary>
    /// Gets the wall time of the map, in milliseconds.
    /// </summary>
    public uint WallTime => _map.WallTime;

    /// <summary>
    /// Plays a map of a game folder with the bot as the opening level of a campaign and records the run.
    /// </summary>
    /// <param name="game">The game folder.</param>
    /// <param name="map">The map path relative to the Maps folder.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <returns>The playback of the recorded run.</returns>
    public static ReplayPlayback Run(string game, string map, ObjectTable objects)
    {
        string mapsPath = GamePath.Resolve(game, "Maps") ?? throw new InvalidDataException("Could not find the Maps folder.");
        string tablePath = GamePath.Resolve(game, GameTable.CommonPath) ?? throw new InvalidDataException($"Could not find file '{GameTable.CommonPath}'.");
        var level = new LevelLoader(objects).Load(map, Path.Combine(mapsPath, map)) ?? throw new InvalidDataException($"Could not find a route through the map '{map}'.");
        var table = GameTable.Load(tablePath);
        var play = new Playthrough(game, objects, table);
        var result = PlayLevel(play, level.Analysis, map, table);

        return new ReplayPlayback(play, level.Analysis, result);
    }

    /// <summary>
    /// Gets the time of a step of the playthrough.
    /// </summary>
    /// <param name="index">The index of the step, clamped to the valid range of steps.</param>
    /// <returns>The time of the step, in milliseconds.</returns>
    public int GetFrameTime(int index) => _frameTimes[Math.Clamp(index, 0, _frameTimes.Length - 1)];

    /// <summary>
    /// Writes the current frame of the run, with the system cursor drawn at the run's cursor position.
    /// </summary>
    /// <param name="pixels">The pixels of the frame, row by row, at least <see cref="FrameSize"/> of them.</param>
    /// <returns><see langword="true"/> if the pixels were written; <see langword="false"/> if <paramref name="pixels"/> already holds that frame.</returns>
    public bool Render(Bgra32[] pixels)
    {
        if (_rendered == pixels && _renderedTime == _shownTime)
            return false;

        var screen = _map.Session.Graphics.Screen!;
        screen.CopyTo(pixels);

        var size = screen.Size;

        var point = _map.Input.Cursor - _map.ViewCenter + new Vector2(size.Width, size.Height) * 0.5f;
        _map.CursorSprite.ShownCursorImage.Draw(pixels.AsSpan(0, size.Width * size.Height), size, new Point((int)MathF.Round(point.X), (int)MathF.Round(point.Y)));
        _rendered = pixels;
        _renderedTime = _shownTime;
        return true;
    }

    /// <summary>
    /// Attempts to step the run one frame toward a time, restarting the run first if the time is earlier than the current one.
    /// </summary>
    /// <param name="time">The time to step toward, in milliseconds.</param>
    /// <param name="isFull">When this method returns, <see langword="true"/> if the frame was stepped in full; <see langword="false"/> if it was stepped quickly because it is well before <paramref name="time"/>.</param>
    /// <returns><see langword="true"/> if a frame was stepped; <see langword="false"/> if the run is already at <paramref name="time"/> or has ended.</returns>
    public bool TryStepToward(int time, out bool isFull)
    {
        isFull = false;

        if (_isStepped && _shownTime > time)
        {
            _map = Load();
        }

        if (!_map.TryGetNextDemoTime(out int next) || next > time)
            return false;

        isFull = next >= time - DrawnLead;

        return StepFrame(!isFull);
    }

    private static PlaythroughResult PlayLevel(Playthrough play, RouteAnalysis selected, string map, GameTable table)
    {
        string stem = Path.GetFileNameWithoutExtension(map);
        int number = 1;

        if (stem.StartsWith("Level_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(stem.AsSpan("Level_".Length), out int parsed) && parsed > 0)
            number = parsed;

        string folder = Path.GetDirectoryName(map) ?? "";
        var rewards = table.GetArray(GameTable.GetLevelMoneyName(folder));
        int money = CampaignState.StartingMoney + (number > 1 && number <= rewards.Count ? rewards[number - 1] : 0);
        var state = CampaignState.CreateInitial(money);

        return play.PlayCampaign([selected], [], 1, state, record: true).Levels[0];
    }

    private Map Load()
    {
        _now = 0;
        _isStepped = false;
        _shownTime = 0;
        _rendered = null;
        var map = _play.LoadReplay(_analysis, _loadout, _demo);
        _pacer.Restart();
        map.Session.Graphics.Screen = new Screen(Size.Round(map.Session.ScreenSize), true);

        return map;
    }

    private bool StepFrame(bool isAimOnly)
    {
        uint wallTime = _map.TryGetNextDemoTime(out int next) ? _map.WallTime + (uint)(next - _map.Time) : _pacer.TakeNextWallTime(_map);

        bool isQuitting;

        if (isAimOnly)
        {
            isQuitting = _map.StepAimed(wallTime, _now, out _now);
        }
        else
        {
            _map.PostNextDemoMouse(out _);
            isQuitting = _map.Step(wallTime, _now, out _now);
        }

        if (isQuitting || !_map.IsPlayingDemo)
            return false;

        _isStepped = true;
        _shownTime = _map.Time;

        return true;
    }
}

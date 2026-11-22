using System.Drawing;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides playback of a recorded playthrough of one level and shows the frames the engine draws as it replays the run.
/// </summary>
public sealed class ReplayPlayback
{
    private const int FrameInterval = 100;

    private readonly Playthrough _play;
    private readonly RouteAnalysis _analysis;
    private readonly PlayerLoadout _loadout;
    private readonly DemoResource _demo;
    private readonly int[] _frameTimes;
    private Map _map;
    private uint _now;
    private bool _isStepped;
    private bool _isClockDue;
    private int _shownTime;

    private ReplayPlayback(Playthrough play, RouteAnalysis analysis, PlaythroughResult result)
    {
        _play = play;
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
    /// Gets the size of the frames, the game's screen size.
    /// </summary>
    public Size FrameSize => _map.Session.Graphics.Screen!.Size;

    /// <summary>
    /// Simulates a map of a game folder as the opening level of a campaign and records the run.
    /// </summary>
    /// <param name="game">The game folder.</param>
    /// <param name="map">The map path relative to the Maps folder.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <returns>The playback of the simulated level.</returns>
    public static ReplayPlayback Run(string game, string map, ObjectTable objects)
    {
        string mapsPath = GamePath.Resolve(game, "Maps") ?? throw new InvalidDataException("Could not find the Maps folder.");
        string tablePath = GamePath.Resolve(game, GameTable.CommonPath) ?? throw new InvalidDataException($"Could not find file '{GameTable.CommonPath}'.");
        var level = new LevelLoader(objects).Load(map, Path.Combine(mapsPath, map)) ?? throw new InvalidDataException($"Could not find a route through the map '{map}'.");
        var table = GameTable.Load(tablePath);
        var play = new Playthrough(game, objects, table);
        var result = SimulateLevel(play, level.Analysis, map, table);

        return new ReplayPlayback(play, level.Analysis, result);
    }

    /// <summary>
    /// Gets the time of a step of the playthrough.
    /// </summary>
    /// <param name="index">The index of the step, clamped to the valid range of steps.</param>
    /// <returns>The time of the step, in milliseconds.</returns>
    public int GetFrameTime(int index) => _frameTimes[Math.Clamp(index, 0, _frameTimes.Length - 1)];

    /// <summary>
    /// Writes the last frame the engine had drawn by a given time of the run, with the mouse cursor drawn at the game's cursor position.
    /// </summary>
    /// <param name="pixels">The pixels of the frame, row by row, at least <see cref="FrameSize"/> of them.</param>
    /// <param name="time">The time of the frame, in milliseconds.</param>
    /// <param name="cursor">The mouse cursor to draw at the game's cursor position.</param>
    public void Render(Bgra32[] pixels, int time, CursorImage cursor)
    {
        Seek(time);
        var screen = _map.Session.Graphics.Screen!;
        screen.CopyTo(pixels);

        var size = screen.Size;

        var point = _map.Cursor - _map.ViewCenter + new System.Numerics.Vector2(size.Width, size.Height) * 0.5f;
        cursor.Draw(pixels.AsSpan(0, size.Width * size.Height), size, new Point((int)MathF.Round(point.X), (int)MathF.Round(point.Y)));
    }

    private static PlaythroughResult SimulateLevel(Playthrough play, RouteAnalysis selected, string map, GameTable table)
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
        _isClockDue = false;
        _shownTime = 0;
        var map = _play.LoadReplay(_analysis, _loadout, _demo);
        map.Session.Graphics.Screen = new Screen(Size.Round(map.Session.ScreenSize), true);

        return map;
    }

    private void Seek(int time)
    {
        if (_isStepped && _shownTime > time)
        {
            _map = Load();
        }

        while (_map.TryGetNextDemoTime(out int next) && next <= time && StepFrame())
        {
        }
    }

    private bool StepFrame()
    {
        if (_isClockDue)
        {
            if (_map.TryGetNextDemoTime(out int next))
            {
                _map.AdvanceClock(_map.WallTime + (uint)(next - _map.Time));
            }
            else
            {
                _map.AdvanceClock();
            }
        }

        bool drawFrame = _map.StepDemo(_now, out _now);

        if (!_map.IsPlayingDemo)
            return false;

        _map.Step(drawFrame);
        _isStepped = true;
        _isClockDue = true;
        _shownTime = _map.Time;

        return true;
    }
}

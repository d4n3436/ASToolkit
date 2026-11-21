using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides playback of a recorded playthrough of one level, drawing the level as it looked at any time of the run.
/// </summary>
public sealed class ReplayPlayback
{
    private const int ShakeHold = 20;
    private const int FrameInterval = 100;
    private const float MinMove = 1e-6f;

    private readonly Playthrough _play;
    private readonly RouteAnalysis _analysis;
    private readonly PlayerLoadout _loadout;
    private readonly DemoResource _demo;
    private readonly ObjectTable _objects;
    private readonly MapRenderer _renderer;
    private readonly MapGraphics _graphics;
    private readonly List<MergedMapSprite> _baseSprites;
    private readonly int[] _frameTimes;
    private readonly HashSet<MergedMapSprite> _hidden = new(ReferenceEqualityComparer.Instance);
    private readonly List<LiveSprite> _live = [];
    private readonly List<(MergedMapSprite Placement, Sprite Sprite, Vector3 Start)> _statics = [];
    private Map _map;
    private MapObserver _observer;
    private uint _now;
    private bool _isStepped;
    private bool _isClockDue;
    private int _shownTime;
    private int _sceneCreated = -1;
    private List<MergedMapSprite> _sceneSprites = [];
    private MapScene? _scene;

    private ReplayPlayback(Playthrough play, RouteAnalysis analysis, PlaythroughResult result, ReferenceLevel level, ObjectTable objects, MapRenderer renderer)
    {
        _play = play;
        _analysis = analysis;
        _objects = objects;
        _renderer = renderer;
        _graphics = level.Map.Graphics;
        Result = result;
        _loadout = result.StartingLoadout ?? throw new ArgumentException("The playthrough does not include a starting loadout.", nameof(result));
        _demo = result.Demo ?? throw new ArgumentException("The playthrough does not include a recorded demo.", nameof(result));

        _map = Load();
        _observer = new MapObserver(_map);
        int[] times = _map.EnumerateDemoFrameTimes().ToArray();
        
        _frameTimes = times
            .DistinctBy(time => time / FrameInterval)
            .Append(times.Length > 0 ? times[^1] : 0)
            .Distinct()
            .ToArray();

        _baseSprites = level.Map.Sprites
            .Where(sprite => !IsUnit(objects, sprite.Id) && !IsCannon(objects, sprite.Id) && !sprite.Id.IsScripted)
            .ToList();
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
    /// Simulates a map of a game folder as the opening level of a campaign and records the run.
    /// </summary>
    /// <param name="game">The game folder.</param>
    /// <param name="map">The map path relative to the Maps folder.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="renderer">The renderer that draws the map.</param>
    /// <returns>The playback of the simulated level.</returns>
    public static ReplayPlayback Run(string game, string map, ObjectTable objects, MapRenderer renderer)
    {
        string mapsPath = GamePath.Resolve(game, "Maps") ?? throw new InvalidDataException("Could not find the Maps folder.");
        string tablePath = GamePath.Resolve(game, Path.Combine("Maps", "common_table.lgc")) ?? throw new InvalidDataException("Could not find file 'Maps\\common_table.lgc'.");
        var level = new LevelLoader(objects).Load(map, Path.Combine(mapsPath, map)) ?? throw new InvalidDataException($"Could not find a route through the map '{map}'.");
        var table = GameTable.Load(tablePath);
        var play = new Playthrough(game, objects, table);
        var result = SimulateLevel(play, level.Analysis, map, table);

        return new ReplayPlayback(play, level.Analysis, result, level, objects, renderer);
    }

    /// <summary>
    /// Gets the time of a step of the playthrough.
    /// </summary>
    /// <param name="index">The index of the step, clamped to the valid range of steps.</param>
    /// <returns>The time of the step, in milliseconds.</returns>
    public int GetFrameTime(int index) => _frameTimes[Math.Clamp(index, 0, _frameTimes.Length - 1)];

    /// <summary>
    /// Draws the level at a time into a frame centered on the game's camera position, or on a point offset from it.
    /// </summary>
    /// <param name="pixels">The pixels of the frame, row by row.</param>
    /// <param name="size">The size of the frame in pixels.</param>
    /// <param name="scale">The scale of the map in the frame, at most 1.</param>
    /// <param name="time">The time of the frame, in milliseconds.</param>
    /// <param name="cameraOffset">The view's offset from the game's camera in map pixels.</param>
    /// <param name="cursor">The mouse cursor to draw at the game's cursor position.</param>
    public void Render(Bgra32[] pixels, Size size, float scale, int time, Vector2 cameraOffset, CursorImage cursor)
    {
        Seek(time);
        var scene = GetScene();
        _live.Clear();
        AddLiveSprites();
        _renderer.UpdateLive(scene, _live);
        var steady = _map.ViewCenter + cameraOffset;
        var shake = _map.CameraShake;
        var center = steady + (shake is not null && time - shake.Value.Time < ShakeHold ? shake.Value.Offset : Vector2.Zero);
        var halfFrame = new Point(size.Width / 2, size.Height / 2);
        var corner = new Point((int)MathF.Round((center.X - scene.Left) * scale) - halfFrame.X, (int)MathF.Round((center.Y - scene.Top) * scale) - halfFrame.Y);
        var area = new SixLabors.ImageSharp.Rectangle(corner.X, corner.Y, size.Width, size.Height);
        _hidden.Clear();
        foreach (var sprite in _sceneSprites)
        {
            if (_map.IsRemoved(sprite))
            {
                _hidden.Add(sprite);
            }
        }

        foreach (var entry in _statics)
        {
            if (Vector3.DistanceSquared(entry.Sprite.Position, entry.Start) > MinMove)
            {
                _hidden.Add(entry.Placement);
            }
        }

        scene.Render(pixels, size.Width, area, scale, time, _hidden, _map.Gamma);

        var flash = _map.ScreenFlash;
        if (flash is not null)
        {
            ScreenFlashBlend.At(flash.Value.Time, flash.Value.Duration, flash.Value.Color, time)?.Apply(pixels);
        }

        // Draw the system cursor the game uses where the unshaken camera puts it
        var cursorPoint = (_map.Cursor - _map.ViewCenter) * scale;
        cursor.Draw(pixels.AsSpan(0, size.Width * size.Height), size, new Point((int)MathF.Round(cursorPoint.X) + halfFrame.X, (int)MathF.Round(cursorPoint.Y) + halfFrame.Y));
    }

    private static PlaythroughResult SimulateLevel(Playthrough play, RouteAnalysis selected, string map, GameTable table)
    {
        string stem = Path.GetFileNameWithoutExtension(map);
        int number = 1;

        if (stem.StartsWith("Level_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(stem.AsSpan("Level_".Length), out int parsed) && parsed > 0)
            number = parsed;

        string folder = Path.GetDirectoryName(map) ?? "";
        string rewardName = folder.Replace('\\', '/').Trim('/').ToUpperInvariant() switch
        {
            "ADDON" => "LevelMoneyAddon",
            "ADDON2" => "LevelMoneyAddon2",
            "RANDOM" => "LevelMoneyRandom",
            _ => "LevelMoney"
        };
        var rewards = table.GetArray(rewardName);
        int money = CampaignState.StartingMoney + (number > 1 && number <= rewards.Count ? rewards[number - 1] : 0);
        var state = CampaignState.CreateInitial(money);

        return play.PlayCampaign([selected], [], 1, state, record: true).Levels[0];
    }

    private static bool IsCannon(ObjectTable objects, KnownObject id) => objects.TryFind(id, out var obj) && obj.SpriteClass == SpriteClass.Cannon;

    private static bool IsUnit(ObjectTable objects, KnownObject id) =>
        id != KnownObject.Crate && objects.TryFind(id, out var obj) && obj is { MaxHp: > 0, SpriteClass: SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man };

    private Map Load()
    {
        _now = 0;
        _isStepped = false;
        _isClockDue = false;
        _shownTime = 0;
        var map = _play.LoadReplay(_analysis, _loadout, _demo);
        _statics.Clear();
        foreach (var placement in map.Sprites)
        {
            var sprite = map.FindPlacementSprite(placement);

            if (sprite is not null && !placement.Id.IsScripted)
            {
                _statics.Add((placement, sprite, sprite.Position));
            }
        }

        return map;
    }

    private void Seek(int time)
    {
        if (_isStepped && _shownTime > time)
        {
            _map = Load();
            _observer = new MapObserver(_map);
            _scene = null;
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
        _observer.Observe();
        _isStepped = true;
        _isClockDue = true;
        _shownTime = _map.Time;

        return true;
    }

    private MapScene GetScene()
    {
        int created = _map.Sprites.Count - _analysis.Model.Sprites.Count;

        if (_scene is null || created != _sceneCreated)
        {
            _sceneCreated = created;
            _sceneSprites = _baseSprites.Concat(_map.Sprites.Skip(_analysis.Model.Sprites.Count)).ToList();
            _scene = _renderer.CreateSceneWithoutLinks(_sceneSprites, _graphics);
        }

        return _scene;
    }

    private void AddLiveSprites()
    {
        foreach (var sprite in _map.ActiveSprites)
        {
            AddSprite(sprite);
        }

        foreach (var pickup in new PickupTracker(_map).Pickups)
        {
            var body = PickupTracker.IsDropped(pickup) ? _map.FindPlacementSprite(pickup) : null;

            if (body is not null)
            {
                Add(SpawnedObjectSystem.GetState(body));
            }
        }

        foreach (var effect in _observer.BakedEffects)
        {
            Add(effect);
        }

        foreach (var entry in _statics)
        {
            if (Vector3.DistanceSquared(entry.Sprite.Position, entry.Start) > MinMove)
            {
                AddPlacement(entry.Placement, entry.Sprite.Position);
            }
        }
    }

    private void AddSprite(Sprite sprite) =>
        Add(new EffectState(sprite.Definition.Id, sprite.Position, sprite.Direction, sprite.Animation, sprite.CreatedAt, sprite.Frame, sprite.KeyframePosition));

    private void Add(in EffectState state) => AddLiveSprite(state.Id, state.Position, state.Direction, state.Frame, state.Keyframe);

    private void AddPlacement(MergedMapSprite placement, Vector3 position)
    {
        if (_map.IsRemoved(placement))
            return;

        var file = _renderer.GetSprite(placement.Id);

        if (file is null || !_objects.TryFind(placement.Id, out var obj))
            return;

        AddLiveSprite(placement.Id, position, placement.Direction, obj.GetFrame(placement.Direction, file.Value.FrameCount), 0);
    }

    private void AddLiveSprite(KnownObject id, Vector3 position, Angle direction, int frame, float keyframe)
    {
        var file = _renderer.GetSprite(id);

        if (file is null || frame < 0 || frame >= file.Value.FrameCount || !_objects.TryFind(id, out var obj))
            return;

        var gamma = _objects.TryGetWeapon(obj, out var weapon) ? weapon.GetGamma(keyframe).Pack() : default;
        _live.Add(new LiveSprite(id, position, direction, frame, gamma));
    }
}

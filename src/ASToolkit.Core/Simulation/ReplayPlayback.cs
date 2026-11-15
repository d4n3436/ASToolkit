using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rendering;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides the playback of a recorded playthrough of one level, such as its map stages, live sprites, lighting, camera shake and flashes at any time.
/// </summary>
public sealed class ReplayPlayback
{
    private const int ShakeHold = 20;
    private const int FrameInterval = 100;

    private readonly ObjectTable _objects;
    private readonly MapRenderer _renderer;
    private readonly PlaythroughReplay _replay;
    private readonly Stage[] _stages;
    private readonly PlacementTimeline[] _moved;
    private readonly Dictionary<MergedMapSprite, int> _pickedUp;
    private readonly int[] _frameTimes;
    private readonly SizeF _mapSize;
    private readonly Vector2 _startCamera;
    private Vector2[] _cameras = [];
    private SizeF _cameraScreen;
    private readonly HashSet<MergedMapSprite> _hidden = [];
    private readonly List<LiveSprite> _live = [];

    private ReplayPlayback(PlaythroughResult result, ReferenceLevel level, ObjectTable objects, MapRenderer renderer)
    {
        _objects = objects;
        _renderer = renderer;
        Result = result;
        _replay = result.Replay!;

        var moved = _replay.PlacementFrames
            .Select(frame => frame.Placement)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        var sprites = level.Map.Sprites
            .Where(sprite => !IsUnit(objects, sprite.Id) && !IsCannon(objects, sprite.Id) && !sprite.Id.IsScripted && !moved.Contains(sprite))
            .ToList();

        _stages = BuildStages(sprites, result, renderer, level.Map.Graphics, level.Script.BlackoutGamma);

        _moved = level.Map.Sprites
            .Where(moved.Contains)
            .Select(sprite => BuildPlacementTimeline(sprite, _replay))
            .ToArray();

        _pickedUp = _replay.PickupEvents
            .Concat(_replay.RemovedSprites)
            .GroupBy(removal => removal.Placement)
            .ToDictionary(group => group.Key, group => group.Min(removal => removal.Time));

        _frameTimes = _replay.Inputs
            .Select(input => input.Time)
            .DistinctBy(time => time / FrameInterval)
            .Append(_replay.Inputs.Length > 0 ? _replay.Inputs[^1].Time : 0)
            .Distinct()
            .ToArray();
        _mapSize = level.Map.Size;
        _startCamera = level.Map.Scroll;
    }

    /// <summary>
    /// Gets the outcome of the playthrough, holding its replay.
    /// </summary>
    public PlaythroughResult Result { get; }

    /// <summary>
    /// Gets the number of steps of the playthrough, one for every 100 ms of play and its last tick.
    /// </summary>
    public int FrameCount => _frameTimes.Length;

    /// <summary>
    /// Simulates a map of a game folder as the opening level of a campaign.
    /// </summary>
    /// <param name="game">The game folder.</param>
    /// <param name="map">The map path relative to the Maps folder.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="renderer">The renderer drawing the map.</param>
    /// <returns>The playback of the simulated level.</returns>
    public static ReplayPlayback Run(string game, string map, ObjectTable objects, MapRenderer renderer)
    {
        string mapsPath = GamePath.Resolve(game, "Maps") ?? throw new InvalidDataException("The Maps folder was not found");
        string tablePath = GamePath.Resolve(game, Path.Combine("Maps", "common_table.lgc")) ?? throw new InvalidDataException("common_table.lgc was not found");
        var level = new LevelLoader(objects).Load(map, Path.Combine(mapsPath, map)) ?? throw new InvalidDataException($"{map} has no route");
        var table = GameTable.Load(tablePath);
        var result = SimulateLevel(new Playthrough(game, objects, table), level.Analysis, map, table);

        return new ReplayPlayback(result, level, objects, renderer);
    }

    /// <summary>
    /// Gets the time of a step of the playthrough.
    /// </summary>
    /// <param name="index">The index of the step, clamped to the steps there are.</param>
    /// <returns>The time of the step, in milliseconds.</returns>
    public int GetFrameTime(int index) => _frameTimes[Math.Clamp(index, 0, _frameTimes.Length - 1)];

    /// <summary>
    /// Draws the level at a time into a frame centered where the game's camera looks, or on an offset from it.
    /// </summary>
    /// <param name="pixels">The pixels of the frame, row by row.</param>
    /// <param name="size">The size of the frame in pixels.</param>
    /// <param name="scale">The scale of the map in the frame, at most 1.</param>
    /// <param name="time">The time of the frame, in milliseconds.</param>
    /// <param name="cameraOffset">The view's offset from the game's camera in map pixels.</param>
    /// <param name="cursor">The mouse cursor drawn at the recorded cursor position.</param>
    public void Render(Bgra32[] pixels, Size size, float scale, int time, Vector2 cameraOffset, CursorImage cursor)
    {
        var stage = _stages[Math.Max(0, _stages.FindLastAtOrBefore(time))];
        _live.Clear();
        int started = _replay.Tracks.FindLastAtOrBefore(time) + 1;
        for (int track = 0; track < started; track++)
        {
            if (_replay.Tracks[track].End > time)
            {
                var sample = _replay.GetSample(track, time);
                AddLiveSprite(sample.Id, sample.Position, sample.Direction, sample.Frame, sample.Keyframe);
            }
        }

        foreach (var effect in _replay.Scenery)
        {
            if (effect.StartedAt <= time)
            {
                AddLiveSprite(effect.Id, effect.Position, effect.Direction, effect.Frame, effect.Keyframe);
            }
        }

        foreach (var placement in _moved)
        {
            AddPlacement(placement, time);
        }

        var scene = stage.Scene;
        _renderer.UpdateLive(scene, _live);
        var screen = (SizeF)size / scale;
        var steady = GetCamera(screen, time) + screen.ToVector2() / 2 + cameraOffset;
        var center = steady + GetShake(time);
        var halfFrame = new Point(size.Width / 2, size.Height / 2);
        var corner = new Point((int)MathF.Round((center.X - scene.Left) * scale) - halfFrame.X, (int)MathF.Round((center.Y - scene.Top) * scale) - halfFrame.Y);
        var area = new SixLabors.ImageSharp.Rectangle(corner.X, corner.Y, size.Width, size.Height);
        var gamma = stage.Light.At(time);
        _hidden.Clear();
        foreach ((var sprite, int collected) in _pickedUp)
        {
            if (collected <= time)
            {
                _hidden.Add(sprite);
            }
        }

        scene.Render(pixels, size.Width, area, scale, time, _hidden, gamma);
        DrawFlash(pixels, time);

        // Draw the system cursor the game uses where the unshaken camera puts it
        int tick = _replay.Inputs.FindLastAtOrBefore(time);
        var cursorPoint = ((tick < 0 ? steady : _replay.Inputs[tick].Cursor) - steady) * scale;
        cursor.Draw(pixels.AsSpan(0, size.Width * size.Height), size, new Point((int)MathF.Round(cursorPoint.X) + halfFrame.X, (int)MathF.Round(cursorPoint.Y) + halfFrame.Y));
    }

    // Runs the game's camera over every tick for the screen the frame shows, the camera before the first tick first
    private Vector2 GetCamera(SizeF screen, int time)
    {
        var inputs = _replay.Inputs;

        if (screen != _cameraScreen || _cameras.Length != inputs.Length + 1)
        {
            _cameraScreen = screen;
            _cameras = new Vector2[inputs.Length + 1];
            var camera = ScrollCamera.Start(screen, _mapSize, _startCamera);
            _cameras[0] = camera.Position;
            for (int index = 0; index < inputs.Length; index++)
            {
                camera = camera.Update(inputs[index].Player, inputs[index].Cursor, inputs[index].Time - (index > 0 ? inputs[index - 1].Time : 0));
                _cameras[index + 1] = camera.Position;
            }
        }

        return _cameras[inputs.FindLastAtOrBefore(time) + 1];
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

    private static Stage[] BuildStages(List<MergedMapSprite> sprites, PlaythroughResult result, MapRenderer renderer, MapGraphics graphics, uint? blackoutGamma)
    {
        var events = result.Replay!.CreatedSprites
            .GroupBy(creation => creation.Time)
            .Select(group => (Time: group.Key, Added: group.Select(creation => creation.Placement).ToArray(), Light: (Gamma?)null))
            .ToList();

        if (result.Script.BlackoutAt is not null && blackoutGamma is not null)
        {
            events.Add((result.Script.BlackoutAt.Value, [], Gamma.FromScript(blackoutGamma.Value)));
        }
        if (result.Script.DoorsOpenedAt is not null)
        {
            events.Add((result.Script.DoorsOpenedAt.Value, [], default(Gamma)));
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        var initial = graphics.Ambient;
        var light = new LightFade(0, initial, initial);
        var stages = new List<Stage> { new(0, renderer.CreateSceneWithoutLinks(sprites, graphics), light) };
        foreach ((int time, var added, var target) in events)
        {
            if (target is not null)
            {
                light = new LightFade(time, light.At(time), target.Value);
            }
            if (added.Length > 0)
            {
                sprites = sprites.Concat(added).ToList();
            }
            stages.Add(new Stage(time, added.Length > 0 ? renderer.CreateSceneWithoutLinks(sprites, graphics) : stages[^1].Scene, light));
        }

        return stages.ToArray();
    }

    private static PlacementTimeline BuildPlacementTimeline(MergedMapSprite sprite, PlaythroughReplay replay) =>
        new(sprite, replay.PlacementFrames.Where(frame => frame.Placement == sprite).ToArray(),
            replay.RemovedSprites.Where(removal => removal.Placement == sprite).Select(removal => removal.Time).DefaultIfEmpty(int.MaxValue).First());

    // Placed cannons come alive as effects when the level loads
    private static bool IsCannon(ObjectTable objects, KnownObject id) => objects.TryFind(id, out var obj) && obj.SpriteClass == SpriteClass.Cannon;

    private static bool IsUnit(ObjectTable objects, KnownObject id) =>
        id != KnownObject.Crate && objects.TryFind(id, out var obj) && obj is { MaxHp: > 0, SpriteClass: SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man };

    private Vector2 GetShake(int time)
    {
        int at = _replay.Shakes.FindLastAtOrBefore(time);

        return at >= 0 && time - _replay.Shakes[at].Time < ShakeHold ? _replay.Shakes[at].Offset : Vector2.Zero;
    }

    // Keeps one flash slot, restarted by every flash
    private void DrawFlash(Span<Bgra32> pixels, int time)
    {
        int at = _replay.Flashes.FindLastAtOrBefore(time);

        if (at < 0)
            return;

        var flash = _replay.Flashes[at];
        ScreenFlashBlend.At(flash.Time, flash.Duration, flash.Color, time)?.Apply(pixels);
    }

    private void AddPlacement(PlacementTimeline placement, int time)
    {
        if (time >= placement.RemovedAt)
            return;

        int at = placement.Frames.FindLastAtOrBefore(time);
        var sprite = _renderer.GetSprite(placement.Sprite.Id);

        if (sprite is null || !_objects.TryFind(placement.Sprite.Id, out var obj))
            return;

        var position = at >= 0 ? placement.Frames[at].Position : placement.Sprite.Position;
        AddLiveSprite(placement.Sprite.Id, position, placement.Sprite.Direction, obj.GetFrame(placement.Sprite.Direction, sprite.Value.FrameCount), 0);
    }

    private void AddLiveSprite(KnownObject id, Vector3 position, Angle direction, int frame, float keyframe)
    {
        var sprite = _renderer.GetSprite(id);

        if (sprite is null || frame < 0 || frame >= sprite.Value.FrameCount || !_objects.TryFind(id, out var obj))
            return;

        var gamma = _objects.TryGetWeapon(obj, out var weapon) ? weapon.GetGamma(keyframe).Pack() : default;
        _live.Add(new LiveSprite(id, position, direction, frame, gamma));
    }

    private sealed record PlacementTimeline(MergedMapSprite Sprite, PlacementFrame[] Frames, int RemovedAt);

    private sealed record Stage(int Time, MapScene Scene, LightFade Light) : ITimed;
}

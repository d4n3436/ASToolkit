using ASToolkit.Core.Engine;
using System.Diagnostics;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Simulation;
using ASToolkit.Core.Routes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Gui.ViewModels;

/// <summary>
/// Represents the live playback of a playthrough simulation of the map open in the viewer, following the player with a camera.
/// </summary>
public partial class ReplayViewModel : ViewModelBase
{
    private const int DefaultViewportWidth = 1280;
    private const int DefaultViewportHeight = 720;
    private const int ShakeHold = 20;
    private const int FlashPasses = 3;
    private const int FlashRiseParts = 9;
    private const int FullStrength = 256;
    private static readonly KnownObject[] DoorIds = [KnownObject.SmallDoor, KnownObject.DoubleDoor];

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private PlaythroughReplay? _replay;
    private IReadOnlyList<Stage> _stages = [];
    private IReadOnlyList<DoorTimeline> _doors = [];
    private IReadOnlyDictionary<MergedMapSprite, int> _pickedUp = new Dictionary<MergedMapSprite, int>();
    private readonly HashSet<MergedMapSprite> _hidden = [];
    private readonly List<LiveSprite> _live = [];
    private readonly Dictionary<int, UnitState> _ahead = [];
    private Dictionary<int, FrameHistory> _unitFrames = new();
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
    /// Gets or sets the width of the view in device pixels, filled edge to edge.
    /// </summary>
    public int ViewportWidth { get; set; } = DefaultViewportWidth;

    /// <summary>
    /// Gets or sets the height of the view in device pixels, filled edge to edge.
    /// </summary>
    public int ViewportHeight { get; set; } = DefaultViewportHeight;

    /// <summary>
    /// Gets or sets the camera's offset from the player in map pixels, or null while the camera follows the player.
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
            var (result, stages, doors) = await Task.Run(() => Simulate(game, map, objects, renderer));
            if (version != _loadVersion)
            {
                return;
            }

            var replay = result.Replay!;
            _replay = replay;
            _stages = stages;
            _doors = doors;
            _pickedUp = replay.PickupEvents
                .Concat(replay.RemovedSprites)
                .GroupBy(removal => removal.Placement)
                .ToDictionary(group => group.Key, group => group.Min(removal => removal.Time));
            _unitFrames = replay.UnitFrames
                .GroupBy(frame => frame.Unit)
                .ToDictionary(group => group.Key, group => new FrameHistory(group.Select(frame => frame.Time).ToArray(), group.Select(frame => frame.Frame).ToArray()));
            
            FrameCount = replay.Frames.Count;
            FrameIndex = 0;
            _time = 0;
            _baseTime = 0;
            Zoom = Math.Max(ViewportWidth / (float)DefaultViewportWidth, ViewportHeight / (float)DefaultViewportHeight);
            CameraOffset = null;
            IsFollowing = true;
            IsReady = true;
            IsPlaying = replay.Frames.Count > 0;
            _clock.Restart();
            Status = $"{(result.Survived ? "survived" : "died")}  progress {result.Progress:P0}  kills {result.Kills}  {replay.Frames.Count} frames";
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

        var replay = _replay;
        if (replay is not null && replay.Frames.Count > 0)
        {
            _time = replay.Frames[Math.Clamp(value, 0, replay.Frames.Count - 1)].Time;
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
    /// Sets the camera's offset from the player by a screen drag, in device pixels, and stops it from following the player.
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
        var replay = _replay;
        if (!IsPlaying || replay is null || replay.Frames.Count == 0)
        {
            return;
        }

        _time = Math.Min(replay.Frames[^1].Time, _baseTime + (int)(_clock.ElapsedMilliseconds * Speed));
        int index = FrameIndex;
        while (index < replay.Frames.Count - 1 && replay.Frames[index + 1].Time <= _time)
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
        if (index >= replay.Frames.Count - 1)
        {
            _time = replay.Frames[index].Time;
            IsPlaying = false;
        }
    }

    private static (PlaythroughResult Result, IReadOnlyList<Stage> Stages, IReadOnlyList<DoorTimeline> Doors) Simulate(string game, string map,
        ObjectTable objects, MapRenderer renderer)
    {
        string mapsPath = GamePath.Resolve(game, "Maps") ?? throw new InvalidDataException("The Maps folder was not found");
        string? tablePath = GamePath.Resolve(game, Path.Combine("Maps", "common_table.lgc"));
        if (tablePath is null)
        {
            throw new InvalidDataException("common_table.lgc was not found");
        }

        var level = new LevelLoader(objects).Load(map, Path.Combine(mapsPath, map)) ?? throw new InvalidDataException($"{map} has no route");
        var table = GameTable.Load(tablePath);
        var play = new Playthrough(game, objects, table);
        var result = SimulateLevel(play, level.Analysis, map, table);

        var sprites = level.Map.Sprites
            .Where(sprite => !IsUnit(objects, sprite.Id) && !sprite.Id.IsScripted)
            .ToList();
        
        var stages = BuildStages(sprites, result, renderer, level.Map.Graphics, level.Script.BlackoutTint);
        
        var doors = level.Map.Sprites
            .Where(sprite => IsDoor(sprite.Id))
            .Select(sprite => BuildDoorTimeline(sprite, result.Replay!))
            .ToArray();
        
        return (result, stages, doors);

        bool IsDoor(KnownObject id) => DoorIds.Contains(id);
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

    private static Stage[] BuildStages(List<MergedMapSprite> sprites, PlaythroughResult result, MapRenderer renderer, MapGraphics graphics,
        uint? blackoutTint)
    {
        var events = result.Replay!.CreatedSprites
            .GroupBy(creation => creation.Time)
            .Select(group => (Time: group.Key, Added: group.Select(creation => creation.Placement).ToArray(), Light: (PaletteTint?)null))
            .ToList();

        if (result.Script.BlackoutAt is not null && blackoutTint is not null)
        {
            events.Add((result.Script.BlackoutAt.Value, [], PaletteTint.FromScriptGamma(blackoutTint.Value)));
        }
        if (result.Script.DoorsOpenedAt is not null)
        {
            events.Add((result.Script.DoorsOpenedAt.Value, [], default(PaletteTint)));
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        var initial = new PaletteTint(graphics.AmbientNegative, graphics.AmbientPositive);
        var light = new LightFade(0, initial, initial);
        var stages = new List<Stage> { new(0, renderer.CreateScene(sprites, graphics), light) };
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
            stages.Add(new Stage(time, added.Length > 0 ? renderer.CreateScene(sprites, graphics) : stages[^1].Scene, light));
        }

        return stages.ToArray();
    }

    private void DrawCursor(int width, int height, int x, int y)
    {
        if (!_cursor.IsValid)
            return;

        for (int row = 0; row < _cursor.Height; row++)
        {
            int target = y - _cursor.HotspotY + row;

            if ((uint)target >= (uint)height)
                continue;

            for (int column = 0; column < _cursor.Width; column++)
            {
                int across = x - _cursor.HotspotX + column;

                if ((uint)across < (uint)width && _cursor.TryGetPixel(column, row, out var color))
                {
                    _pixels[target * width + across] = new Bgra32(color.R, color.G, color.B, byte.MaxValue);
                }
            }
        }
    }

    private static DoorTimeline BuildDoorTimeline(MergedMapSprite sprite, PlaythroughReplay replay) =>
        new(sprite, replay.DoorFrames.Where(frame => frame.Placement == sprite).ToArray());

    private static bool IsUnit(ObjectTable objects, KnownObject id) =>
        id != KnownObject.Crate && objects.TryFind(id, out var obj) && obj is { MaxHp: > 0, SpriteClass: SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Player };

    private static Vector2 GetShake(IReadOnlyList<CameraShake> shakes, int time)
    {
        int low = 0;
        int high = shakes.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;

            if (shakes[middle].Time <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high >= 0 && time - shakes[high].Time < ShakeHold ? shakes[high].Offset : Vector2.Zero;
    }

    private void DrawFlash(IReadOnlyList<ScreenFlash> flashes, int time)
    {
        foreach (var flash in flashes)
        {
            int elapsed = time - flash.Time;

            if (elapsed < 0 || elapsed >= flash.Duration)
                continue;

            int rise = flash.Duration / FlashRiseParts;
            int strength = elapsed < rise
                ? FlashRiseParts * FullStrength * elapsed / flash.Duration
                : FullStrength * (flash.Duration - elapsed) / (flash.Duration - rise);
            int red = (flash.Color >> 16 & 0xff) * strength >> 8;
            int green = (flash.Color >> 8 & 0xff) * strength >> 8;
            int blue = (flash.Color & 0xff) * strength >> 8;
            for (int index = 0; index < _pixels.Length; index++)
            {
                ref var pixel = ref _pixels[index];
                int r = pixel.R;
                int g = pixel.G;
                int b = pixel.B;
                for (int pass = 0; pass < FlashPasses; pass++)
                {
                    r = Math.Min(byte.MaxValue, r + r * red / byte.MaxValue);
                    g = Math.Min(byte.MaxValue, g + g * green / byte.MaxValue);
                    b = Math.Min(byte.MaxValue, b + b * blue / byte.MaxValue);
                }

                pixel = new Bgra32((byte)r, (byte)g, (byte)b, pixel.A);
            }
        }
    }

    private void DrawFrame()
    {
        var replay = _replay;
        var stages = _stages;
        var doors = _doors;
        var renderer = Source?.Renderer;
        var objects = Source?.Objects;
        if (replay is null || stages.Count == 0 || renderer is null || objects is null || replay.Frames.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(FrameIndex, 0, replay.Frames.Count - 1);
        var frame = replay.Frames[index];
        var next = replay.Frames[Math.Min(index + 1, replay.Frames.Count - 1)];
        float share = next.Time > frame.Time ? Math.Clamp((_time - frame.Time) / (float)(next.Time - frame.Time), 0, 1) : 0;
        var stage = stages[0];
        for (int at = stages.Count - 1; at > 0; at--)
        {
            if (stages[at].Time <= _time)
            {
                stage = stages[at];
                break;
            }
        }

        _live.Clear();
        _ahead.Clear();
        foreach (var unit in next.Units)
        {
            _ahead[unit.Unit] = unit;
        }

        foreach (var unit in frame.Units)
        {
            int spriteFrame = GetUnitFrame(unit);

            if (spriteFrame != UnitFrame.Deleted)
            {
                AddLiveSprite(_live, objects, renderer, unit.Id, GetPosition(unit), unit.Facing, unit.Animation, _time, 0, player: unit, spriteFrame: spriteFrame, keyframe: unit.Keyframe);
            }
        }

        foreach (var effect in GetEffects(replay, frame, _time))
        {
            AddLiveSprite(_live, objects, renderer, effect.Id, effect.Position, effect.Facing, effect.Animation, _time, 0, spriteFrame: effect.Frame, keyframe: effect.Keyframe);
        }
        foreach (var effect in replay.Scenery)
        {
            if (effect.StartedAt <= _time)
            {
                AddLiveSprite(_live, objects, renderer, effect.Id, effect.Position, effect.Facing, effect.Animation, _time, 0, spriteFrame: effect.Frame, keyframe: effect.Keyframe);
            }
        }

        foreach (var door in doors)
        {
            AddDoor(_live, objects, renderer, door, _time);
        }

        var scene = stage.Scene;
        renderer.UpdateLive(scene, _live);
        var player = GetPosition(frame.Units[0]);
        var center = player.AsVector2() + CameraOffset.GetValueOrDefault() + GetShake(replay.Shakes, _time);
        float renderScale = (float)Math.Min(1, Zoom);
        int bufferWidth = Zoom <= 1 ? ViewportWidth : (int)(ViewportWidth / Zoom);
        int bufferHeight = Zoom <= 1 ? ViewportHeight : (int)(ViewportHeight / Zoom);
        int left = (int)MathF.Round((center.X - scene.Left) * renderScale) - bufferWidth / 2;
        int top = (int)MathF.Round((center.Y - scene.Top) * renderScale) - bufferHeight / 2;
        var area = new SixLabors.ImageSharp.Rectangle(left, top, bufferWidth, bufferHeight);
        if (_pixels.Length != bufferWidth * bufferHeight)
        {
            _pixels = new Bgra32[bufferWidth * bufferHeight];
        }

        var tint = stage.Light.At(_time);
        _hidden.Clear();
        foreach ((var sprite, int collected) in _pickedUp)
        {
            if (collected <= _time)
            {
                _hidden.Add(sprite);
            }
        }

        scene.Render(_pixels, bufferWidth, area, renderScale, _time, _hidden, tint.Negative, tint.Positive);
        DrawFlash(replay.Flashes, _time);

        var cursor = frame.Cursor + (next.Cursor - frame.Cursor) * share;
        DrawCursor(bufferWidth, bufferHeight, (int)MathF.Round((cursor.X - scene.Left) * renderScale) - left, (int)MathF.Round((cursor.Y - scene.Top) * renderScale) - top);
        Frame = Upload(bufferWidth, bufferHeight);
        return;

        int GetUnitFrame(UnitState unit)
        {
            if (!_unitFrames.TryGetValue(unit.Unit, out var history))
                return unit.Frame;

            int found = Array.BinarySearch(history.Times, _time);
            int at = found >= 0 ? found : ~found - 1;

            return at < 0 ? unit.Frame : history.Frames[at];
        }

        Vector3 GetPosition(UnitState unit) => _ahead.TryGetValue(unit.Unit, out var ahead)
            ? unit.Position + (ahead.Position - unit.Position) * share
            : unit.Position;
    }

    private static void AddDoor(List<LiveSprite> live, ObjectTable objects, MapRenderer renderer, DoorTimeline door, int time)
    {
        int low = 0, high = door.Frames.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (door.Frames[middle].Time <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        float height = high >= 0 ? door.Frames[high].Position.Z : door.Sprite.Position.Z;
        var position = door.Sprite.Position with { Z = height };
        if (!objects.TryFind(door.Sprite.Id, out var obj))
        {
            return;
        }

        var sprite = renderer.GetSprite(door.Sprite.Id);
        int frameIndex = sprite is null ? 0 : obj.GetFrame(door.Sprite.Facing, sprite.Value.FrameCount);
        live.Add(new LiveSprite(door.Sprite.Id, position, door.Sprite.Facing, frameIndex));
    }

    // Adds the recorded engine frame when there is one, then the linked object at its offset and facing
    private static void AddLiveSprite(List<LiveSprite> live, ObjectTable objects, MapRenderer renderer, KnownObject id, Vector3 position, byte dir,
        SpriteAnimation animation, int time, int depth, UnitState? player = null, int spriteFrame = -1, float keyframe = 0)
    {
        if (depth > MapRenderer.MaxAttachDepth || !objects.TryFind(id, out var obj))
        {
            return;
        }

        var sprite = renderer.GetSprite(id);
        if (sprite is null)
        {
            return;
        }

        int direction = obj.GetDirection(dir);
        var visual = obj.GetVisualAnimation(animation);
        int frameCount = Math.Max(1, obj.FrameCounts[(int)visual]);
        int duration = Math.Max(1, obj.GetFrameDuration(animation, sprite.Value.Speed));
        int step = animation is SpriteAnimation.Stand or SpriteAnimation.Go ? time / duration % frameCount : Math.Min(frameCount - 1, time / duration);
        int frameIndex = spriteFrame >= 0 ? spriteFrame : obj.GetStateStart(visual) + direction * frameCount + step;
        if (frameIndex < sprite.Value.FrameCount)
        {
            var tint = depth == 0 && objects.TryGetWeapon(obj, out var weapon) ? PaletteTint.FromKeyframes(weapon, keyframe) : default;
            live.Add(new LiveSprite(id, position, dir, frameIndex, tint));
        }

        if (player is not null && player.Value.Weapon != 0)
            return;

        var link = obj.Link;
        if (link == 0 || !objects.TryFind(link, out var attached))
            return;

        var offset = obj.GetLinkOffset(attached, sprite, frameIndex, dir);
        AddLiveSprite(live, objects, renderer, link, position + offset, dir, animation, time, depth + 1);
    }

    private static IReadOnlyList<EffectState> GetEffects(PlaythroughReplay replay, PlaythroughFrame frame, int time)
    {
        var history = replay.EffectFrames;
        if (history.Count == 0)
            return frame.Effects;

        int low = 0;
        int high = history.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (history[middle].Time <= time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low == 0 ? [] : history[low - 1].Effects;
    }

    // Alternates between two bitmaps to give the bound Frame property a new reference without allocating
    private unsafe WriteableBitmap Upload(int width, int height)
    {
        var bitmap = _back;
        if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        using (var buffer = bitmap.Lock())
        {
            for (int row = 0; row < height; row++)
            {
                _pixels.AsSpan(row * width, width).CopyTo(new Span<Bgra32>((byte*)buffer.Address + row * buffer.RowBytes, width));
            }
        }

        _back = _front;
        _front = bitmap;
        return bitmap;
    }

    private sealed record FrameHistory(int[] Times, int[] Frames);

    private sealed record DoorTimeline(MergedMapSprite Sprite, IReadOnlyList<DoorFrame> Frames);

    private sealed record Stage(int Time, MapScene Scene, LightFade Light);

}

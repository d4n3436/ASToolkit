using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PointF = System.Drawing.PointF;
using ASToolkit.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Renders maps the way the game engines draw them.
/// </summary>
public sealed class MapRenderer
{
    internal const int Margin = 4;
    private const int StripRows = 16;
    internal const int MaxStripWorkers = 8;
    private const int PoolMegabytes = 32;
    private const int MissingSize = 8;
    private const int FlatDepth = 0x400;
    private const int MaxFlatDepth = 0x7fff;
    private const int DepthPerHeight = 8;
    private const float MeshTolerance = 0.01f;
    private const int RaisedDepth = 0x3fff;
    private const int SequelRaisedHeight = 1000;
    private const int MaxAttachDepth = 4;
    private const uint PlayerArmy = 0;
    private const uint ArmyMask = 3;
    private const uint SequelArmyMask = 7;
    private const int LightPass = 7;
    private const float LightBufferShare = 0.3f;
    private const float LightFalloff = 0.2f;
    private const float LightFalloffBias = 0.04f;
    private const float LightFalloffScale = 256 / 5f;
    private const float LightTextureRowScale = 0.7070602f;
    internal const float LightQuadDrop = 10;
    private const int LightAlphaReference = 2;
    private const float OctagonalMajor = 0.961f;
    private const float OctagonalMinor = 0.398f;
    private const float AngleUnitsPerRadian = 40.74366760253906f;
    private const int HalfTurn = 0x80;
    private const float FlatShadowRatio = 1.8f;
    private const float FlatShadowPush = 0.6f;
    private const float TopShadowHeight = 0.3f;
    private const float InitialLowestHeight = 999;
    private const int ActionStackCount = 4;
    private const int PauseAct = 40;
    private const int FlagmanTriggerAct = 43;
    private const int StopStackAct = 73;
    private const int WhileNotQuestAct = 77;
    private const int VariableWaitAct = 79;
    private const int SetInvisibleAct = 98;
    private const float RowScale = 0.70706f;
    private const uint PhaseSeedStep = 0x9e3779b9;
    private const int PlayerPartCount = 12;
    private const int FemalePartOffset = KnownObject.PlayerStartGirl - KnownObject.PlayerStart;
    private const uint PhaseSeedFactor = 2654435761;
    
    private static readonly MemoryAllocator Allocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = PoolMegabytes });
    private static readonly Configuration ImageConfiguration = CreateConfiguration(Environment.ProcessorCount);
    private static readonly float[] Sine = Enumerable.Range(0, 256).Select(angle => (float)MathF.Sin(angle * MathF.PI / 128)).ToArray();

    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<string, VidResource?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<FrameKey, SpriteFrame?> _frames = new();
    private readonly ConcurrentDictionary<(string Path, PackedTint Tint, bool TintsAlpha), Rgba32[]> _palettes = new();
    private readonly ConcurrentDictionary<(string Path, int Frame), bool[]> _masks = new();
    private readonly ConcurrentDictionary<string, VidSurface[]> _surfaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MapRenderer"/> class.
    /// </summary>
    public MapRenderer(string gameFolder, ObjectTable objects)
    {
        _gameFolder = gameFolder;
        _objects = objects;
    }

    /// <summary>
    /// Gets a value indicating whether sprites in the groups the level scripts remove during play are left out, which shows
    /// the map with those areas open.
    /// </summary>
    public bool OmitRemovableGroups { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the objects the game keeps hidden are left out, such as map editor helpers and the
    /// Alien Shooter 2 sprites that hide themselves when the level starts.
    /// </summary>
    public bool OmitHiddenObjects { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether Alien Shooter maps show the player the way the game shows a female player.
    /// </summary>
    public bool ShowFemalePlayer { get; init; }

    /// <summary>
    /// Gets a value indicating whether the black covers of Alien Shooter 2 maps are left out, which shows the objects
    /// outside the playable area.
    /// </summary>
    public bool OmitCovers { get; init; }

    /// <summary>
    /// Gets a value indicating whether Alien Shooter 2 maps are lit as the game lights them, with the ambient light of the map
    /// and the omni-lights of its objects instead of leaving the colors as they are.
    /// </summary>
    public bool MatchGameLighting { get; init; }

    /// <summary>
    /// Gets a value indicating whether the ambient light keeps its brightness but loses its hue when the game lighting is matched.
    /// </summary>
    public bool OmitAmbientTint { get; init; }

    /// <summary>
    /// Gets the color of the image where no sprite is drawn.
    /// </summary>
    public static Rgb24 Background { get; } = new(0, 0, 0);

    /// <summary>
    /// Renders map sprites and the objects the engine attaches to them, scaled down by a factor of at most 1. The ambient
    /// light of the graphics record only takes effect with <see cref="MatchGameLighting"/>.
    /// </summary>
    public MapImage Render(IReadOnlyList<MergedMapSprite> sprites, float scale = 1, MapGraphics graphics = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 1);
        var scene = CreateScene(sprites, graphics, false);
        if (scene.IsEmpty)
        {
            return new MapImage { Image = new Image<Rgb24>(ImageConfiguration, 640, 360, Background), OriginX = 0, OriginY = 0, Scale = scale, Undrawn = scene.Undrawn };
        }

        var image = new Image<Rgb24>(ImageConfiguration, Math.Max(1, (int)(scene.Width * scale)), Math.Max(1, (int)(scene.Height * scale)), Background);
        scene.Render((row, pixels) => pixels.CopyTo(image.DangerousGetPixelRowMemory(row).Span), new Rectangle(0, 0, image.Width, image.Height), scale, null);
        return new MapImage { Image = image, OriginX = scene.Left, OriginY = scene.Top, Scale = scale, Undrawn = scene.Undrawn };
    }

    /// <summary>
    /// Creates the animated scene of map sprites and the objects the engine attaches to them. The ambient light of the graphics
    /// record only takes effect with <see cref="MatchGameLighting"/>.
    /// </summary>
    public MapScene CreateScene(IReadOnlyList<MergedMapSprite> sprites, MapGraphics graphics = default) => CreateScene(sprites, graphics, true);

    /// <summary>
    /// Returns the map sprites that the game never shows, such as script dummies, monster generators and blocks.
    /// </summary>
    public HelperMarker[] GetHelpers(IReadOnlyList<MergedMapSprite> sprites) => 
        sprites.Where(IsHelper)
            .Select(sprite => new HelperMarker(sprite, GetHelperKind(sprite.Id)))
            .ToArray();

    /// <summary>
    /// Returns the sprite of an object, or null when the object or its sprite file is missing.
    /// </summary>
    public VidResource? GetSprite(KnownObject id) => _objects.TryFind(id, out _) ? GetFile(_objects.GetPath(id)) : null;

    /// <summary>
    /// Draws one frame of the sprite of an object over a background color, the way the engine draws it into the map, or the
    /// light of the frame on the background for an omni-light. Returns null when the frame cannot be decoded.
    /// </summary>
    public Image<Rgb24>? DrawFrame(KnownObject id, int frameIndex, Rgb24 background)
    {
        if (!_objects.TryFind(id, out var obj))
        {
            return null;
        }

        string path = _objects.GetPath(id);
        var file = GetFile(path);
        var tint = PackedTint.Pack(obj.GammaRed, obj.GammaGreen, obj.GammaBlue, obj.GammaAlpha);
        if (_objects.IsAlienShooter2)
        {
            tint = tint.Add(tint);
        }

        if (file is not null && file.Value.Flags.HasFlag(VidFlags.OmniLight))
        {
            return DrawLight(obj, file.Value.GetLightColor(frameIndex), background);
        }

        var frame = file is null ? null : GetFrame(path, obj, file.Value, frameIndex, 0, tint, GetObjectScale(obj, file));
        if (frame is null)
        {
            return null;
        }

        var image = new Image<Rgb24>(ImageConfiguration, frame.Width, frame.Height, background);
        var placement = new Placement(obj, frame, frame.Width / 2f, frame.Height / 2f, frame.Width, frame.Height, 0, false, 0, null!, null);
        var state = new PlacementState(frame, 0, 0, 0);
        var canvas = new Canvas([placement], [state], [], [], new Rectangle(0, 0, frame.Width, frame.Height), 1, frame.Width, frame.Height, null,
            (row, pixels) => pixels.CopyTo(image.DangerousGetPixelRowMemory(row).Span), background);
        canvas.DrawAll();
        return image;
    }

    private MapScene CreateScene(IReadOnlyList<MergedMapSprite> sprites, MapGraphics graphics, bool animate)
    {
        var scene = new SceneObjects(new List<Placement>(sprites.Count), [], [], animate);
        var ambient = MatchGameLighting && _objects.IsAlienShooter2 ? GetAmbient(graphics) : (PackedTint?)null;
        int undrawn = 0;
        foreach (var sprite in sprites)
        {
            if (OmitRemovableGroups && IsRemovableGroup(sprite.Payload) ||
                OmitCovers && IsCover(sprite.Id) ||
                OmitHiddenObjects && _objects.IsAlienShooter2 && StartsHidden(sprite.Payload))
            {
                continue;
            }
            
            var look = new SpriteLook(GetPlacementTint(sprite), sprite.Scale);
            undrawn += Place(scene, sprite, GetPlayerPart(sprite.Id), sprite.Position, sprite.Facing, look, ambient, 0);
        }

        var lights = scene.Lights.Select(light => light.CastsShadows ? light with { Shadows = GetShadows(light, scene.Casters) } : light).ToArray();
        return new MapScene(scene.Placed.ToArray(), lights, ambient, undrawn);
    }

    private bool IsHelper(MergedMapSprite sprite) =>
        !_objects.TryFind(sprite.Id, out var obj) || GetFile(_objects.GetPath(sprite.Id)) is null || IsHiddenByArmy(obj, sprite.Army);

    private HelperKind GetHelperKind(KnownObject id)
    {
        if (!_objects.TryFind(id, out var obj) || GetFile(_objects.GetPath(id)) is null)
        {
            return HelperKind.Missing;
        }

        return obj.SpriteClass switch
        {
            SpriteClass.Building => HelperKind.Spawner,
            SpriteClass.Sprite => HelperKind.Trigger,
            SpriteClass.Scenery => HelperKind.Blocker,
            _ => HelperKind.Other
        };
    }

    private bool IsCover(KnownObject id) =>
        _objects.IsAlienShooter2 && id is KnownObject.SoftCover or KnownObject.BlackBlock or KnownObject.SoftCoverDummy;

    private static bool StartsHidden(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2 * sizeof(int))
        {
            return false;
        }

        int lastStack = MemoryMarshal.Read<int>(payload) & 0xffff;
        int stackCount = Math.Min(MemoryMarshal.Read<int>(payload[sizeof(int)..]), ActionStackCount);
        int actionSize = Unsafe.SizeOf<MapAction>();
        Span<(int Start, int Length)> stacks = stackalloc (int, int)[ActionStackCount];
        int position = 2 * sizeof(int);
        for (int stack = 0; stack < stackCount; stack++)
        {
            if (position + sizeof(int) > payload.Length)
            {
                return false;
            }

            int count = MemoryMarshal.Read<int>(payload[position..]);
            position += sizeof(int);
            if (count < 0 || position + count * actionSize > payload.Length)
            {
                return false;
            }

            stacks[stack] = (position, count * actionSize);
            position += count * actionSize;
        }

        bool hidden = false;
        for (int step = 1; step <= ActionStackCount; step++)
        {
            (int start, int length) = stacks[(lastStack + step) & (ActionStackCount - 1)];
            hidden = ApplyHidingBeforeWaiting(payload.Slice(start, length), actionSize, hidden);
        }

        return hidden;
    }

    private static bool ApplyHidingBeforeWaiting(ReadOnlySpan<byte> actions, int actionSize, bool hidden)
    {
        for (int offset = actions.Length - actionSize; offset >= 0; offset -= actionSize)
        {
            var action = MemoryMarshal.Read<MapAction>(actions[offset..]);
            if (action.Act is PauseAct or FlagmanTriggerAct or StopStackAct or WhileNotQuestAct or VariableWaitAct)
            {
                break;
            }

            if (action.Act == SetInvisibleAct)
            {
                hidden = action.Var1 != 0;
            }
        }

        return hidden;
    }

    private static bool IsRemovableGroup(ReadOnlySpan<byte> payload)
    {
        int end = payload.Length - 1;
        if (end < 1 || payload[end] != 0)
        {
            return false;
        }

        int start = end;
        while (start > 0 && payload[start - 1] != 0)
        {
            start--;
        }

        if (end - start < 2 || payload[start] != (byte)'h')
        {
            return false;
        }

        for (int index = start + 1; index < end; index++)
        {
            if (payload[index] is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    private int Place(SceneObjects scene, MergedMapSprite owner, KnownObject id, Vector3 position, byte dir, SpriteLook look, PackedTint? ambient,
        int attachDepth)
    {
        if (!_objects.TryFind(id, out var obj) || (OmitHiddenObjects && IsHiddenByArmy(obj, owner.Army)))
        {
            return 0;
        }

        string path = _objects.GetPath(id);
        var file = GetFile(path);
        int frameIndex = file is null ? 0 : obj.GetFrame(dir, file.Value.FrameCount);
        bool sequel = _objects.IsAlienShooter2;
        bool isOmniLight = sequel && file is not null && file.Value.Flags.HasFlag(VidFlags.OmniLight);
        int sequelPass = isOmniLight ? LightPass : sequel && file is not null ? GetPass(obj, file.Value) : 0;
        var objectTint = PackedTint.Pack(obj.GammaRed, obj.GammaGreen, obj.GammaBlue, obj.GammaAlpha);
        var tint = look.Tint ?? objectTint;
        if (sequel)
        {
            tint = tint.Add(objectTint);
        }

        if (ambient.HasValue && sequelPass >= LightPass && !obj.Flags.HasFlag(ObjectFlags.Unlit))
        {
            tint = tint.Add(ambient.Value);
        }

        var scale = look.Scale * GetObjectScale(obj, file);
        var timing = scene.Animate ? GetTiming(obj, file, frameIndex, owner.Handle, attachDepth) : AnimationTiming.Static;
        if (isOmniLight && ambient.HasValue)
        {
            uint[] colors = Enumerable.Range(frameIndex, timing.Count).Select(file!.Value.GetLightColor).ToArray();
            AddLight(scene.Lights, obj, colors, timing, tint, position, look.Scale);
        }

        if (ambient.HasValue && sequel && file is not null && IsShadowCaster(obj, file.Value))
        {
            scene.Casters.Add(new ShadowCaster(obj, path, file.Value, position, dir, frameIndex, scale));
        }

        byte rotation = file is null ? (byte)0 : GetRotation(obj, file.Value, path, dir);
        var frame = file is null ? null : GetFrame(path, obj, file.Value, frameIndex, rotation, tint, scale);
        (int width, int height) = frame is not null ? (frame.Width, frame.Height) : file is null ? (MissingSize, MissingSize) : (file.Value.Width, file.Value.Height);
        int pass = sequel && file is not null ? sequelPass : frame is null || !frame.IsCombined ? 0 : 1;
        float bob = scene.Animate && obj.Flags.HasFlag(ObjectFlags.Bobbing) ? obj.StepHeight : 0;
        var animation = file is null || (timing.Count == 1 && bob == 0) ? null
            : new FrameAnimation(Enumerable.Range(frameIndex, timing.Count).Select(index => GetFrame(path, obj, file.Value, index, rotation, tint, scale)).ToArray(),
                timing, bob, position.Z, sequel);
        var source = new SceneObject(id, owner, position, dir);
        scene.Placed.Add(new Placement(obj, frame, position.X, position.Y - position.Z, width, height, GetBaseDepth(obj, position.Z, sequel), !sequel && obj.Size.Y < obj.Size.Z, pass, source, animation));
        int undrawn = file is not null && frame is null ? 1 : 0;
        if (obj.Link == 0 || attachDepth >= MaxAttachDepth || !_objects.TryFind(obj.Link, out var attached))
        {
            return undrawn;
        }

        var offset = GetLinkOffset(obj, attached, file, frameIndex, dir);
        return undrawn + Place(scene, owner, GetPlayerPart(obj.Link), position + offset, dir, SpriteLook.Default, ambient, attachDepth + 1);
    }

    // Brighten the background like a light buffer at neutral gray
    private static Image<Rgb24>? DrawLight(GameObject obj, uint color, Rgb24 background)
    {
        var radius = 0.5f * obj.Size * obj.Scale;
        if (radius.X < 1 || radius.Y < 1)
        {
            return null;
        }

        float scale = GetLightScale(radius.Y);
        (int red, int green, int blue) = ((int)((color >> 16 & 0xff) * scale), (int)((color >> 8 & 0xff) * scale), (int)((color & 0xff) * scale));
        var image = new Image<Rgb24>(ImageConfiguration, (int)MathF.Ceiling(2 * radius.X), (int)MathF.Ceiling(2 * radius.Y), background);
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var pixels = rows.GetRowSpan(y);
                float v = (y + 0.5f - rows.Height / 2f) / radius.Y;
                for (int x = 0; x < pixels.Length; x++)
                {
                    int falloff = GetFalloff((x + 0.5f - pixels.Length / 2f) / radius.X, v);
                    pixels[x] = new Rgb24(Brighten(background.R, falloff * red / 255), Brighten(background.G, falloff * green / 255),
                        Brighten(background.B, falloff * blue / 255));
                }
            }
        });

        return image;
    }

    private static byte Brighten(byte color, int light) => (byte)Math.Min(255, color + 2 * color * light / 255);

    private static AnimationTiming GetTiming(GameObject obj, VidResource? file, int frameIndex, uint handle, int attachDepth)
    {
        if (file is null || obj.SpriteClass is SpriteClass.Scenery or SpriteClass.Frame)
        {
            return AnimationTiming.Static;
        }

        int count = Math.Min(obj.FramesPerDirection, file.Value.FrameCount - frameIndex);
        int duration = obj.GetFrameDuration(SpriteAnimation.Stand, file.Value.Speed);
        if (count <= 1 || duration <= 0)
        {
            return AnimationTiming.Static;
        }

        uint seed = (handle + (uint)attachDepth * PhaseSeedStep) * PhaseSeedFactor;
        int phase = obj.Flags.HasFlag(ObjectFlags.StartsOnFirstFrame) ? 0 : (int)((seed >> 8) % (uint)count);
        return new AnimationTiming(count, duration, phase);
    }
    
    private bool IsHiddenByArmy(GameObject obj, uint army) =>
        obj.Flags.HasFlag(ObjectFlags.ArmyDependent) && (GetArmyBits(army) != PlayerArmy || (!_objects.IsAlienShooter2 && obj.SpriteClass == SpriteClass.Scenery));
    
    private KnownObject GetPlayerPart(KnownObject id)
    {
        if (_objects.IsAlienShooter2)
        {
            return id;
        }

        int male = (int)id >= (int)KnownObject.PlayerStartGirl && (int)id <= (int)KnownObject.PlayerStartGirl + PlayerPartCount - 1
            ? (int)id - FemalePartOffset : (int)id;
        if (male is < (int)KnownObject.PlayerStart or >= (int)KnownObject.PlayerStart + PlayerPartCount)
        {
            return id;
        }

        return (KnownObject)(ShowFemalePlayer ? male + FemalePartOffset : male);
    }

    private uint GetArmyBits(uint army) => army & (_objects.IsAlienShooter2 ? SequelArmyMask : ArmyMask);

    internal static Vector3 GetLinkOffset(GameObject owner, GameObject attached, VidResource? file, int frameIndex, byte dir)
    {
        var offset = file is not null && file.Value.Flags.HasFlag(VidFlags.FrameVector)
            ? file.Value.GetFrameVector(frameIndex)
            : owner.LinkOffset with { Y = -owner.LinkOffset.Y };
        if (attached.Flags.HasFlag(ObjectFlags.FixedOffset))
        {
            return offset;
        }

        (float sin, float cos) = MathF.SinCos(dir * MathF.PI / 128);
        return new Vector3((float)(offset.X * cos - offset.Y * sin), (float)(offset.X * sin + offset.Y * cos), offset.Z);
    }

    internal static int GetBaseDepth(GameObject obj, float z, bool sequel)
    {
        int depth = (int)(z * DepthPerHeight);
        if (!obj.Flags.HasFlag(ObjectFlags.Raised))
        {
            return depth;
        }

        return sequel ? depth + SequelRaisedHeight * DepthPerHeight : depth < RaisedDepth ? depth + RaisedDepth : depth;
    }

    private byte GetRotation(GameObject obj, VidResource file, string path, byte dir)
    {
        if (!_objects.IsAlienShooter2 || !obj.Flags.HasFlag(ObjectFlags.Rotated))
        {
            return 0;
        }

        int mode = file.Mode is 0 or -1 && !path.Contains(@"\101.", StringComparison.OrdinalIgnoreCase) ? 1 : file.Mode;
        if (mode == 0 || (mode == -1 && obj.Size.Y < obj.Size.Z))
        {
            return 0;
        }

        if (obj.Flags.HasFlag(ObjectFlags.FreeRotation) || obj.Flags.HasFlag(ObjectFlags.TracksMotion))
        {
            return dir;
        }

        int directions = Math.Max(1, obj.Directions);
        int index = ((dir + 128 / directions) & 255) * directions >> 8;
        return (byte)(dir - (index << 8) / directions);
    }

    private static int GetPass(GameObject obj, VidResource file)
    {
        if (file.Mode == 6)
        {
            return 10;
        }

        if (obj.SpriteClass == SpriteClass.Scenery)
        {
            return file is { IsBlended: false, Blend: VidBlend.Alpha } ? 0 : 2;
        }

        if (obj.SpriteClass == SpriteClass.Region)
        {
            return 5;
        }

        if (obj.SpriteClass is SpriteClass.Frame or SpriteClass.Text)
        {
            return 11;
        }

        if (file.Blend is VidBlend.AdditiveLight or VidBlend.Brighten)
        {
            return 7;
        }

        if (file is { IsBlended: false, Blend: VidBlend.Alpha or VidBlend.AlphaNoDepthWrite })
        {
            return 3;
        }

        return !obj.Flags.HasFlag(ObjectFlags.GroundDecal) && file.Blend != VidBlend.AlphaDepthWrite ? 9 : 4;
    }

    private static Configuration CreateConfiguration(int threads)
    {
        var configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = threads;
        configuration.MemoryAllocator = Allocator;
        return configuration;
    }

    private VidResource? GetFile(string relative) => _files.GetOrAdd(relative, r =>
    {
        string? path = GamePath.Resolve(_gameFolder, r);
        return path is not null && path.EndsWith(".vid", StringComparison.OrdinalIgnoreCase) ? new VidResource(File.ReadAllBytes(path)) : null;
    });

    private static PackedTint? GetPlacementTint(MergedMapSprite sprite) =>
        sprite is { NegativeTint: 0, PositiveTint: 0 } ? null : new PackedTint(sprite.NegativeTint, sprite.PositiveTint);

    private PackedTint GetAmbient(MapGraphics graphics)
    {
        var ambient = new PackedTint(graphics.AmbientNegative, graphics.AmbientPositive);
        return OmitAmbientTint ? ambient.ToGray() : ambient;
    }

    private static void AddLight(List<OmniLight> lights, GameObject obj, uint[] colors, AnimationTiming timing, PackedTint tint, Vector3 position, Vector3 scale)
    {
        var radius = 0.5f * obj.Size * obj.Scale * scale;
        if (colors.All(color => color == 0) || radius.X <= 0 || radius.Y <= 0)
        {
            return;
        }

        var lightColors = colors.Select(color => color == 0 ? Vector3.Zero : new Vector3(tint.Apply((int)(color >> 16 & 0xff), 16),
            tint.Apply((int)(color >> 8 & 0xff), 8), tint.Apply((int)(color & 0xff), 0))).ToArray();
        lights.Add(new OmniLight(position, radius.X, radius.Y, lightColors, timing, obj.Flags.HasFlag(ObjectFlags.Windblown), []));
    }

    // Color scale of the light quad, the falloff 10 units above the ground
    private static float GetLightScale(float radiusY) => LightFalloff / (MathF.Pow(LightQuadDrop / radiusY, 2) + LightFalloff);

    private static int GetFalloff(float x, float y)
    {
        float row = y / LightTextureRowScale;
        return (int)MathF.Round(Math.Clamp((LightFalloff / (x * x + row * row + LightFalloffBias) - LightFalloff) * LightFalloffScale, 0, 255));
    }

    private Vector3 GetObjectScale(GameObject obj, VidResource? file) =>
        !_objects.IsAlienShooter2 && file is not null && file.Value.HasDepth && file.Value.Flags.HasFlag(VidFlags.Surface) ? Vector3.One : obj.Scale;

    private static bool IsShadowCaster(GameObject obj, VidResource file) =>
        obj.Flags.HasFlag(ObjectFlags.Windblown) && file.Flags.HasFlag(VidFlags.Cadr) && !file.Flags.HasFlag(VidFlags.OmniLight)
        && !file.Flags.HasFlag(VidFlags.Dxt) && file is { IsBlended: false, Blend: VidBlend.Alpha };

    private ShadowCard[] GetShadows(OmniLight light, List<ShadowCaster> casters)
    {
        var cards = new List<ShadowCard>();
        foreach (var caster in casters)
        {
            var offset = Vector3.Abs(caster.Position - light.Position);
            if (GetOctagonalLength(GetOctagonalLength(offset.X, offset.Y), offset.Z) < light.RadiusX)
            {
                var card = GetShadowCard(light, caster);
                if (card is not null)
                {
                    cards.Add(card);
                }
            }
        }

        return cards.ToArray();
    }

    private static float GetOctagonalLength(float first, float second) =>
        first < second ? second * OctagonalMajor + first * OctagonalMinor : first * OctagonalMajor + second * OctagonalMinor;

    private ShadowCard? GetShadowCard(OmniLight light, ShadowCaster caster)
    {
        var (obj, file) = (caster.Object, caster.File);
        float towardLight = MathF.Atan2(light.Position.X - caster.Position.X, -(light.Position.Y - caster.Position.Y));
        byte away = (byte)((int)(towardLight * AngleUnitsPerRadian) + HalfTurn);
        bool rotated = obj.Flags.HasFlag(ObjectFlags.Rotated);
        int index = rotated ? obj.GetFrame((byte)(caster.Dir - away), file.FrameCount) : caster.FrameIndex;
        var frame = file.GetFrame(index);
        (float highest, float lowest) = GetHeightRange(frame.Mesh);
        if (frame.RowCount == 0 || frame.Mesh.VertexCount == 0 || highest <= 0)
        {
            return null;
        }

        var first = rotated ? file.GetFrame(0) : frame;
        bool flat = GetHeightRange(first.Mesh).Highest * FlatShadowRatio < Math.Max(first.Width, first.RowCount);
        byte turn = away;
        if (flat && rotated)
        {
            turn = obj.Flags.HasFlag(ObjectFlags.FreeRotation) ? caster.Dir : (byte)(caster.Dir - (obj.GetDirection((byte)(caster.Dir - away)) << 8) / Math.Max(1, obj.Directions));
        }

        float lift = Math.Max(0, caster.Position.Z);
        var scale = caster.Scale;

        var origin = ToScreen(0, 0);
        var columnStep = (ToScreen(frame.Width, 0) - origin) / frame.Width;
        var rowStep = (ToScreen(0, frame.RowCount) - origin) / frame.RowCount;
        float baseDepth = DepthPerHeight * caster.Position.Z + FlatDepth;
        return new ShadowCard(GetMask(caster.Path, file, index), frame.Width, frame.RowCount, origin, columnStep, rowStep,
            DepthPerHeight * highest * TopShadowHeight * scale.Z + baseDepth, DepthPerHeight * lowest * scale.Z + baseDepth);

        Vector2 ToScreen(float column, float row)
        {
            var local = new Vector2((column - file.Width / 2 + frame.Left) * scale.X, (row - lift - file.Height / 2 + frame.Top) * scale.Y);
            if (!flat || rotated)
            {
                local = Turn(local, turn);
            }

            if (flat)
            {
                float push = highest * FlatShadowPush;
                local += new Vector2(push * Sin(away), -push * (float)RowScale * Cos(away));
            }

            return new Vector2(caster.Position.X + local.X, (caster.Position.Y - caster.Position.Z) + local.Y + lift);
        }
    }

    private static (float Highest, float Lowest) GetHeightRange(VidMesh mesh)
    {
        (float highest, float lowest) = (0, InitialLowestHeight);
        for (int index = 0; index < mesh.VertexCount; index++)
        {
            float height = mesh.GetVertex(index).Z;
            highest = Math.Max(highest, height);
            lowest = Math.Min(lowest, height);
        }

        return (highest, lowest);
    }

    private static Vector2 Turn(Vector2 point, byte angle) =>
        new(point.X * Cos(angle) - point.Y * Sin(angle), point.X * Sin(angle) + point.Y * Cos(angle));

    private static float Sin(byte angle) => MathF.Sin(angle * MathF.Tau / 256);

    private static float Cos(byte angle) => MathF.Cos(angle * MathF.Tau / 256);

    private bool[] GetMask(string path, VidResource file, int index) => _masks.GetOrAdd((path, index), _ =>
    {
        var frame = file.GetFrame(index);
        bool[] mask = new bool[frame.Width * frame.RowCount];
        foreach (var run in frame)
        {
            for (int offset = 0; offset < run.Count; offset++)
            {
                int column = run.X + offset - frame.Left;
                int row = run.Y - frame.Top;
                if ((uint)column < (uint)frame.Width && (uint)row < (uint)frame.RowCount)
                {
                    mask[row * frame.Width + column] = true;
                }
            }
        }

        return mask;
    });

    private SpriteFrame? GetFrame(string path, GameObject obj, VidResource view, int index, byte rotation, PackedTint tint, Vector3 scale) =>
        _frames.GetOrAdd(new FrameKey(path, tint, index, rotation, scale), _ => rotation == 0 && scale == Vector3.One
            ? DecodeFrame(path, obj, view, index, tint)
            : DecodeTransformedFrame(path, obj, view, index, rotation, tint, scale));

    private SpriteFrame? DecodeTransformedFrame(string path, GameObject obj, VidResource view, int index, byte rotation, PackedTint tint, Vector3 scale)
    {
        var upright = GetFrame(path, obj, view, index, 0, tint, Vector3.One);
        return upright is null ? null : TransformFrame(upright, view.GetFrame(index).Mesh, rotation, scale);
    }

    private SpriteFrame? DecodeFrame(string path, GameObject obj, VidResource view, int index, PackedTint tint)
    {
        if (index >= view.FrameCount)
        {
            return null;
        }

        if (view.Flags.HasFlag(VidFlags.Surface) && !view.Flags.HasFlag(VidFlags.Cadr))
        {
            return DecodeTextureFrame(path, view, index, tint);
        }

        bool paletted = view.HasPalette;
        if (paletted && view.Palette.Length < 1024)
        {
            return null;
        }

        bool tintsAlpha = view is { IsBlended: true, Blend: VidBlend.Alpha or VidBlend.AlphaNoDepthWrite or VidBlend.AlphaDepthWrite };
        bool sequel = _objects.IsAlienShooter2;
        var palette = paletted ? _palettes.GetOrAdd((path, tint, tintsAlpha), _ => ApplyTint(view.Palette, tint, tintsAlpha, sequel)) : null;
        var frame = view.GetFrame(index);
        int count = 0;
        foreach (var run in frame)
        {
            count += run.Count;
        }

        bool meshDepth = !view.HasDepth && frame.Mesh.TriangleCount > 0;
        float[] meshHeights = meshDepth ? RasterizeHeights(frame.Mesh, view.Width, view.Height) : [];
        var pixelColors = palette ?? new Rgba32[count];
        var result = new SpriteFrame
        {
            Xs = new short[count],
            Indices = paletted ? new byte[count] : [],
            Depths = view.HasDepth || meshDepth ? new ushort[count] : [],
            Colors = pixelColors,
            Width = view.Width,
            Height = view.Height,
            Top = frame.Top,
            Rows = frame.RowCount,
            RowStarts = new int[frame.RowCount + 1],
            IsBlended = view.IsBlended,
            Blend = view.Blend,
            WritesDepth = view.Blend != VidBlend.AlphaNoDepthWrite,
            TestsDepth = TestsDepth(view),
            IsPaletted = paletted
        };
        int row = 0;
        int pixelIndex = 0;
        foreach (var run in frame)
        {
            while (run.Y - frame.Top >= row)
            {
                result.RowStarts[row++] = pixelIndex;
            }

            for (int runIndex = 0; runIndex < run.Count; runIndex++, pixelIndex++)
            {
                result.Xs[pixelIndex] = (short)(run.X + runIndex);
                if (paletted)
                {
                    result.Indices[pixelIndex] = (byte)run.GetColor(runIndex);
                }
                else
                {
                    var color = DecodeColor(view.Format, run, runIndex);
                    pixelColors[pixelIndex] = sequel ? ModulateColor(color, tint, tintsAlpha) : color;
                }

                if (view.HasDepth)
                {
                    result.Depths[pixelIndex] = run.GetDepth(runIndex);
                }
                else if (meshDepth)
                {
                    result.Depths[pixelIndex] = GetMeshDepth(meshHeights, view.Width, run.X + runIndex, run.Y);
                }
            }
        }

        while (row <= frame.RowCount)
        {
            result.RowStarts[row++] = count;
        }

        return result;
    }

    private SpriteFrame? DecodeTextureFrame(string path, VidResource view, int index, PackedTint tint)
    {
        var surfaces = _surfaces.GetOrAdd(path, _ => view.DecodeSurfaces());
        if (!view.TryGetTextureFrame(index, out var entry) || (uint)entry.Surface >= (uint)surfaces.Length || entry.Width <= 0 || entry.Height <= 0)
        {
            return null;
        }

        var surface = surfaces[entry.Surface];
        var pixels = surface.Pixels;
        bool tintsAlpha = view is { IsBlended: true, Blend: VidBlend.Alpha or VidBlend.AlphaNoDepthWrite or VidBlend.AlphaDepthWrite };
        bool sequel = _objects.IsAlienShooter2;
        int left = entry.Left;
        int top = entry.Top;
        var xs = new List<short>();
        var colors = new List<Rgba32>();
        int[] rowStarts = new int[entry.Height + 1];
        for (int row = 0; row < entry.Height; row++)
        {
            rowStarts[row] = xs.Count;
            int sourceY = entry.Y + row;
            for (int column = 0; column < entry.Width && sourceY < surface.Height; column++)
            {
                int sourceX = entry.X + column;
                if (sourceX >= surface.Width)
                {
                    break;
                }

                var color = DecodeA4R4G4B4(pixels[sourceY * surface.Width + sourceX]);
                if (color.A > 0)
                {
                    xs.Add((short)(left + column));
                    colors.Add(sequel ? ModulateColor(color, tint, tintsAlpha) : color);
                }
            }
        }

        rowStarts[entry.Height] = xs.Count;
        return new SpriteFrame
        {
            Xs = xs.ToArray(),
            Indices = [],
            Depths = [],
            Colors = colors.ToArray(),
            Width = view.Width,
            Height = view.Height,
            Top = top,
            Rows = entry.Height,
            RowStarts = rowStarts,
            IsBlended = true,
            Blend = view.Blend,
            WritesDepth = false,
            TestsDepth = TestsDepth(view),
            IsPaletted = false
        };
    }

    // Turn the depth test off for light glows, blend 7 and mode 6 sprites like Alien Shooter 2. Keep it on for unlit renders,
    // which have no light buffer to take the glows.
    private bool TestsDepth(VidResource view) =>
        !_objects.IsAlienShooter2 || !MatchGameLighting || !(view.Blend is VidBlend.AdditiveLight or VidBlend.AlphaDepthWrite || view.Mode == 6);

    internal static float[] RasterizeHeights(VidMesh mesh, int width, int height)
    {
        float[] heights = new float[width * height];
        Array.Fill(heights, float.NaN);
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            var (a, b, c) = mesh.GetTriangle(triangle);
            float area = (b.Y - c.Y) * (float)(a.X - c.X) + (c.X - b.X) * (float)(a.Y - c.Y);
            if (Math.Abs(area) < 1e-9f)
            {
                continue;
            }

            int minX = Math.Max(0, Math.Min(a.X, Math.Min(b.X, c.X)));
            int maxX = Math.Min(width - 1, Math.Max(a.X, Math.Max(b.X, c.X)));
            int minY = Math.Max(0, Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            int maxY = Math.Min(height - 1, Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float weightA = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / area;
                    float weightB = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / area;
                    float weightC = 1 - weightA - weightB;
                    if (weightA < -MeshTolerance || weightB < -MeshTolerance || weightC < -MeshTolerance)
                    {
                        continue;
                    }

                    float z = (float)(weightA * a.Z + weightB * b.Z + weightC * c.Z);
                    ref float cell = ref heights[y * width + x];
                    if (float.IsNaN(cell) || z > cell)
                    {
                        cell = z;
                    }
                }
            }
        }

        return heights;
    }

    private static ushort GetMeshDepth(float[] heights, int width, int x, int y)
    {
        float z = (uint)(y * width + x) < (uint)heights.Length ? heights[y * width + x] : float.NaN;
        return (ushort)Math.Clamp(FlatDepth + (float.IsNaN(z) ? 0 : (int)MathF.Round(z * DepthPerHeight)), 0, ushort.MaxValue);
    }

    private static SpriteFrame TransformFrame(SpriteFrame source, VidMesh mesh, byte rotation, Vector3 scale)
    {
        (float sin, float cos) = MathF.SinCos(rotation * MathF.PI / 128);
        int sourceWidth = source.Width;
        int sourceHeight = source.Height;
        float sourceCenterX = sourceWidth / 2.0f;
        float sourceCenterY = sourceHeight / 2.0f;
        int count = source.Xs.Length;
        int[] lookup = new int[sourceWidth * sourceHeight];
        Array.Fill(lookup, -1);
        float[] heights = new float[count];
        float extentX = 1;
        float extentY = 1;
        for (int row = 0; row < source.Rows; row++)
        {
            int y = source.Top + row;
            for (int index = source.RowStarts[row]; index < source.RowStarts[row + 1]; index++)
            {
                int x = source.Xs[index];
                if ((uint)x < (uint)sourceWidth && (uint)y < (uint)sourceHeight)
                {
                    lookup[y * sourceWidth + x] = index;
                }

                heights[index] = source.Depths.Length > 0 ? (source.Depths[index] - FlatDepth) / (float)DepthPerHeight : 0;
                (float turnedX, float turnedY) = TurnPoint(x + 0.5f - sourceCenterX, y + 0.5f - sourceCenterY, heights[index], sin, cos, scale);
                extentX = Math.Max(extentX, Math.Abs(turnedX));
                extentY = Math.Max(extentY, Math.Abs(turnedY));
            }
        }

        int width = 2 * (int)MathF.Ceiling(extentX) + 2;
        int height = 2 * (int)MathF.Ceiling(extentY) + 2;
        float centerX = width / 2.0f;
        float centerY = height / 2.0f;
        int[] target = new int[width * height];
        Array.Fill(target, -1);
        for (int y = 0; y < height; y++)
        {
            float groundY = (y + 0.5f - centerY) / RowScale;
            for (int x = 0; x < width; x++)
            {
                float turnedX = x + 0.5f - centerX;
                int sourceX = (int)MathF.Floor((turnedX * cos + groundY * sin) / scale.X + sourceCenterX);
                int sourceY = (int)MathF.Floor(RowScale * (groundY * cos - turnedX * sin) / scale.Y + sourceCenterY);
                if ((uint)sourceX < (uint)sourceWidth && (uint)sourceY < (uint)sourceHeight)
                {
                    int index = lookup[sourceY * sourceWidth + sourceX];
                    if (index >= 0 && heights[index] == 0)
                    {
                        target[y * width + x] = index;
                    }
                }
            }
        }

        if (mesh.TriangleCount > 0)
        {
            float[] targetHeights = new float[width * height];
            Array.Fill(targetHeights, float.NegativeInfinity);
            for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
            {
                var (a, b, c) = mesh.GetTriangle(triangle);
                PlaceTriangle([a, b, c], sin, cos, scale, sourceCenterX, sourceCenterY, sourceWidth, sourceHeight, centerX, centerY, width, height, lookup, target, targetHeights);
            }
        }

        return BuildFrame(source, target, width, height, scale.Z);
    }

    private static PointF TurnPoint(float x, float y, float height, float sin, float cos, Vector3 scale)
    {
        float groundX = x * scale.X;
        float groundY = (y + height) / RowScale * scale.Y;
        return new PointF(groundX * cos - groundY * sin, RowScale * (groundX * sin + groundY * cos) - height * scale.Z);
    }

    private static void PlaceTriangle(VidVertex[] vertices, float sin, float cos, Vector3 scale, float sourceCenterX, float sourceCenterY, int sourceWidth, int sourceHeight,
        float centerX, float centerY, int width, int height, int[] lookup, int[] target, float[] targetHeights)
    {
        var points = new PointF[3];
        for (int vertex = 0; vertex < 3; vertex++)
        {
            (float turnedX, float turnedY) = TurnPoint(vertices[vertex].X - sourceCenterX, vertices[vertex].Y - sourceCenterY, vertices[vertex].Z, sin, cos, scale);
            points[vertex] = new PointF(turnedX + centerX, turnedY + centerY);
        }

        float area = (points[1].Y - points[2].Y) * (points[0].X - points[2].X) + (points[2].X - points[1].X) * (points[0].Y - points[2].Y);
        if (Math.Abs(area) < 1e-9f)
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(Math.Min(points[0].X, Math.Min(points[1].X, points[2].X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(Math.Max(points[0].X, Math.Max(points[1].X, points[2].X))));
        int minY = Math.Max(0, (int)MathF.Floor(Math.Min(points[0].Y, Math.Min(points[1].Y, points[2].Y))));
        int maxY = Math.Min(height - 1, (int)MathF.Ceiling(Math.Max(points[0].Y, Math.Max(points[1].Y, points[2].Y))));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float weightA = ((points[1].Y - points[2].Y) * (px - points[2].X) + (points[2].X - points[1].X) * (py - points[2].Y)) / area;
                float weightB = ((points[2].Y - points[0].Y) * (px - points[2].X) + (points[0].X - points[2].X) * (py - points[2].Y)) / area;
                float weightC = 1 - weightA - weightB;
                if (weightA < -MeshTolerance || weightB < -MeshTolerance || weightC < -MeshTolerance)
                {
                    continue;
                }

                float z = (float)(weightA * vertices[0].Z + weightB * vertices[1].Z + weightC * vertices[2].Z);
                int sourceX = (int)MathF.Floor(weightA * vertices[0].X + weightB * vertices[1].X + weightC * vertices[2].X);
                int sourceY = (int)MathF.Floor(weightA * vertices[0].Y + weightB * vertices[1].Y + weightC * vertices[2].Y);
                int cell = y * width + x;
                if ((uint)sourceX >= (uint)sourceWidth || (uint)sourceY >= (uint)sourceHeight || z <= targetHeights[cell])
                {
                    continue;
                }

                int index = lookup[sourceY * sourceWidth + sourceX];
                if (index >= 0)
                {
                    target[cell] = index;
                    targetHeights[cell] = z;
                }
            }
        }
    }

    private static SpriteFrame BuildFrame(SpriteFrame source, int[] target, int width, int height, float scaleZ)
    {
        int top = 0;
        while (top < height && Array.FindIndex(target, top * width, width, index => index >= 0) < 0)
        {
            top++;
        }

        int bottom = height;
        while (bottom > top && Array.FindIndex(target, (bottom - 1) * width, width, index => index >= 0) < 0)
        {
            bottom--;
        }

        int count = target.Count(index => index >= 0);
        short[] xs = new short[count];
        byte[] indices = source.IsPaletted ? new byte[count] : [];
        ushort[] depths = source.Depths.Length > 0 ? new ushort[count] : [];
        var colors = source.IsPaletted ? source.Colors : new Rgba32[count];
        int[] rowStarts = new int[bottom - top + 1];
        int pixel = 0;
        for (int row = 0; row < bottom - top; row++)
        {
            rowStarts[row] = pixel;
            for (int x = 0; x < width; x++)
            {
                int index = target[(top + row) * width + x];
                if (index < 0)
                {
                    continue;
                }

                xs[pixel] = (short)x;
                if (source.IsPaletted)
                {
                    indices[pixel] = source.Indices[index];
                }
                else
                {
                    colors[pixel] = source.Colors[index];
                }

                if (depths.Length > 0)
                {
                    depths[pixel] = (ushort)Math.Clamp(FlatDepth + (source.Depths[index] - FlatDepth) * scaleZ, 0, ushort.MaxValue);
                }

                pixel++;
            }
        }

        rowStarts[bottom - top] = pixel;
        return new SpriteFrame
        {
            Xs = xs,
            Indices = indices,
            Depths = depths,
            Colors = colors,
            Width = width,
            Height = height,
            Top = top,
            Rows = bottom - top,
            RowStarts = rowStarts,
            IsBlended = source.IsBlended,
            Blend = source.Blend,
            WritesDepth = source.WritesDepth,
            TestsDepth = source.TestsDepth,
            IsPaletted = source.IsPaletted
        };
    }

    private static Rgba32 DecodeColor(VidPixelFormat format, VidRun run, int index) => format switch
    {
        VidPixelFormat.A8R8G8B8 or VidPixelFormat.X8R8G8B8 => DecodeA8R8G8B8(run.GetColor32(index), format == VidPixelFormat.A8R8G8B8),
        VidPixelFormat.A4R4G4B4 => DecodeA4R4G4B4(run.GetColor(index)),
        VidPixelFormat.A1R5G5B5 or VidPixelFormat.X1R5G5B5 => DecodeA1R5G5B5(run.GetColor(index), format == VidPixelFormat.A1R5G5B5),
        _ => DecodeR5G6B5(run.GetColor(index))
    };

    private static Rgba32 DecodeR5G6B5(ushort value) => new(Expand(value >> 11 & 0x1f, 5), Expand(value >> 5 & 0x3f, 6), Expand(value & 0x1f, 5));

    private static Rgba32 DecodeA1R5G5B5(ushort value, bool hasAlpha) =>
        new(Expand(value >> 10 & 0x1f, 5), Expand(value >> 5 & 0x1f, 5), Expand(value & 0x1f, 5), (byte)(!hasAlpha || (value & 0x8000) != 0 ? 255 : 0));

    private static Rgba32 DecodeA4R4G4B4(ushort value) =>
        new((byte)((value >> 8 & 0xf) << 4), (byte)((value >> 4 & 0xf) << 4), (byte)((value & 0xf) << 4), (byte)((value >> 12 & 0xf) << 4));

    private static Rgba32 DecodeA8R8G8B8(uint value, bool hasAlpha) =>
        new((byte)(value >> 16), (byte)(value >> 8), (byte)value, hasAlpha ? (byte)(value >> 24) : (byte)255);

    private static Rgba32 ModulateColor(Rgba32 color, PackedTint tint, bool tintsAlpha) =>
        new((byte)tint.Modulate(color.R, 16), (byte)tint.Modulate(color.G, 8), (byte)tint.Modulate(color.B, 0),
            tintsAlpha ? (byte)((color.A * (255 - tint.GetNegative(24)) + 127) / 255) : color.A);

    private static byte Expand(int value, int bits) => (byte)((value << (8 - bits)) | (value >> (2 * bits - 8)));

    private static Rgba32[] ApplyTint(ReadOnlyMemory<byte> palette, PackedTint tint, bool tintsAlpha, bool sequel)
    {
        var colors = new Rgba32[256];
        var entries = palette.Span;
        for (int index = 0; index < 256; index++)
        {
            byte alpha = entries[4 * index + 3];
            if (sequel)
            {
                colors[index] = ModulateColor(new Rgba32(entries[4 * index + 2], entries[4 * index + 1], entries[4 * index], alpha), tint, tintsAlpha);
                continue;
            }

            colors[index] = new Rgba32(
                (byte)tint.Apply(entries[4 * index + 2], 16),
                (byte)tint.Apply(entries[4 * index + 1], 8),
                (byte)tint.Apply(entries[4 * index], 0),
                tintsAlpha ? (byte)(alpha * (256 - tint.GetNegative(24)) >> 8) : alpha);
        }

        return colors;
    }

    internal delegate void RowWriter(int row, ReadOnlySpan<Rgb24> pixels);

    private readonly record struct FrameKey(string Path, PackedTint Tint, int Frame, byte Rotation, Vector3 Scale);

    private readonly record struct SpriteLook(PackedTint? Tint, Vector3 Scale)
    {
        public static SpriteLook Default { get; } = new(null, Vector3.One);
    }

    internal readonly record struct PackedTint(uint Negative, uint Positive)
    {
        public static PackedTint Pack(int red, int green, int blue, int alpha) =>
            new(GetMagnitude(-alpha) << 24 | GetMagnitude(-red) << 16 | GetMagnitude(-green) << 8 | GetMagnitude(-blue),
                GetMagnitude(alpha) << 24 | GetMagnitude(red) << 16 | GetMagnitude(green) << 8 | GetMagnitude(blue));

        public PackedTint Add(PackedTint other) => new(AddBytes(Negative, other.Negative), AddBytes(Positive, other.Positive));

        public PackedTint ToGray() => new(GrayBytes(Negative), GrayBytes(Positive));

        public int GetNegative(int shift) => (int)(Negative >> shift & 0xff);

        public int GetPositive(int shift) => (int)(Positive >> shift & 0xff);

        public int Modulate(int color, int shift) => Math.Min(255, (color * (255 - GetNegative(shift)) + 127) / 255 + GetPositive(shift));

        public int Apply(int color, int shift) => Math.Min(255, (color * (256 - GetNegative(shift)) >> 8) + GetPositive(shift));

        private static uint GetMagnitude(int value) => (uint)Math.Clamp(value, 0, 255);

        private static uint AddBytes(uint first, uint second)
        {
            uint sum = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                sum |= Math.Min(255, (first >> shift & 0xff) + (second >> shift & 0xff)) << shift;
            }

            return sum;
        }

        private static uint GrayBytes(uint value)
        {
            uint gray = ((value >> 16 & 0xff) + (value >> 8 & 0xff) + (value & 0xff) + 1) / 3;
            return (value & 0xff000000) | gray << 16 | gray << 8 | gray;
        }
    }

    internal readonly record struct AnimationTiming(int Count, int Duration, int Phase)
    {
        public static AnimationTiming Static { get; } = new(1, 1, 0);

        public int GetIndex(int time) => Count == 1 ? 0 : (Phase + time / Duration) % Count;
    }

    internal sealed record FrameAnimation(SpriteFrame?[] Frames, AnimationTiming Timing, float Bob, float Z, bool Sequel);

    internal readonly record struct PlacementState(SpriteFrame? Frame, int X, int Y, int BaseDepth);

    internal sealed record OmniLight(Vector3 Position, float RadiusX, float RadiusY, Vector3[] Colors, AnimationTiming Timing, bool CastsShadows,
        ShadowCard[] Shadows)
    {
        public Vector3 GetColor(int? time) => Colors[time is not null ? Timing.GetIndex(time.Value) : 0];
    }

    private sealed record ShadowCaster(GameObject Object, string Path, VidResource File, Vector3 Position, byte Dir, int FrameIndex, Vector3 Scale);

    internal sealed record ShadowCard(bool[] Mask, int Columns, int Rows, Vector2 Origin, Vector2 ColumnStep, Vector2 RowStep, float TopDepth, float BottomDepth);

    private sealed record SceneObjects(List<Placement> Placed, List<OmniLight> Lights, List<ShadowCaster> Casters, bool Animate);

    internal sealed record StripBuffers(Rgb24[] Source, int[] Depth, Rgb24[] Light, int[] Sums, Rgb24[] Row);

    // The sprite canvas is centered on X and ScreenY, the world y minus the height
    internal sealed record Placement(GameObject Object, SpriteFrame? Frame, float ScreenX, float ScreenY, int Width, int Height, int BaseDepth, bool IsStanding,
        int Pass, SceneObject? Source, FrameAnimation? Animation)
    {
        public float Left => ScreenX - Width / 2.0f;

        public float Top => ScreenY - Height / 2.0f;

        public PlacementState GetState(int? time, int left, int top)
        {
            if (time is null || Animation is null)
            {
                return new PlacementState(Frame, (int)MathF.Round(Left - left), (int)MathF.Round(Top - top), BaseDepth);
            }

            var frame = Animation.Frames[Animation.Timing.GetIndex(time.Value)];
            (int width, int height) = frame is null ? (Width, Height) : (frame.Width, frame.Height);
            float screenY = ScreenY;
            int baseDepth = BaseDepth;
            int lift = 0;
            if (Animation.Bob != 0)
            {
                float wave = Sine[(time.Value >> 3) & 255] * Animation.Bob;
                if (Animation.Sequel)
                {
                    screenY -= wave;
                    baseDepth = GetBaseDepth(Object, Animation.Z + wave, true);
                }
                else if (!Object.Flags.HasFlag(ObjectFlags.Raised) || (int)(Animation.Z * DepthPerHeight) >= RaisedDepth)
                {
                    int depth = (int)(Sine[(time.Value >> 3) & 255] * (float)Animation.Bob * DepthPerHeight);
                    baseDepth += depth;
                    lift = depth / DepthPerHeight;
                }
            }

            return new PlacementState(frame, (int)MathF.Round(ScreenX - width / 2.0f - left), (int)MathF.Round(screenY - height / 2.0f - top) - lift, baseDepth);
        }

        public int GetAnimationKey(int time) =>
            Animation is null ? 0 : Animation.Timing.GetIndex(time) << 8 | (Animation.Bob == 0 ? 0 : (time >> 3) & 255);

        public IEnumerable<SpriteFrame?> GetFrames() => Animation?.Frames ?? [Frame];

        public int GetBobReach() => Animation is null ? 0 : (int)MathF.Ceiling(Math.Abs(Animation.Bob)) + 1;

        public int GetPixelDepth(SpriteFrame frame, int baseDepth, int row, int index) =>
            frame.Depths.Length > 0 ? baseDepth + frame.Depths[index]
            : IsStanding ? Math.Min(baseDepth + FlatDepth, MaxFlatDepth) + (frame.Rows - row) * DepthPerHeight : Math.Min(baseDepth + FlatDepth, MaxFlatDepth);
    }

    // Draws an output area in strips of rows in parallel. Each strip is drawn at full size with its own depth buffer, then
    // every output pixel takes the average of the source pixels it covers.
    internal sealed class Canvas
    {
        private readonly Placement[] _placed;
        private readonly PlacementState[] _states;
        private readonly List<int>[] _strips;
        private readonly OmniLight[] _lights;
        private readonly Vector3[] _colors;
        private readonly Rectangle _area;
        private readonly float _scale;
        private readonly int _left;
        private readonly int _width;
        private readonly int _height;
        private readonly int[] _columnStarts;
        private readonly int _stripRows;
        private readonly PackedTint? _ambient;
        private readonly RowWriter _writer;
        private readonly Rgb24 _background;

        public Canvas(Placement[] placed, PlacementState[] states, OmniLight[] lights, Vector3[] colors, Rectangle area, float scale, int sceneWidth, int sceneHeight,
            PackedTint? ambient, RowWriter writer, Rgb24 background)
        {
            _placed = placed;
            _states = states;
            _area = area;
            _scale = scale;
            _height = sceneHeight;
            _left = GetSourceStart(area.Left, sceneWidth);
            _width = GetSourceStart(area.Right, sceneWidth) - _left;
            _columnStarts = new int[area.Width + 1];
            for (int column = 0; column <= area.Width; column++)
            {
                _columnStarts[column] = GetSourceStart(area.Left + column, sceneWidth) - _left;
            }

            _stripRows = Math.Max(1, (int)(StripRows * scale));
            _ambient = ambient;
            _writer = writer;
            _background = background;
            _colors = colors;
            var shift = new Vector2(_left, 0);
            _lights = lights.Select(light => light with { Position = light.Position - new Vector3(_left, 0, 0), Shadows = light.Shadows.Select(card => card with { Origin = card.Origin - shift }).ToArray() })
                .ToArray();
            _strips = new List<int>[StripCount];
            for (int strip = 0; strip < _strips.Length; strip++)
            {
                _strips[strip] = [];
            }

            for (int index = 0; index < placed.Length; index++)
            {
                AddToStrips(index);
            }
        }

        public int StripCount => (_area.Height + _stripRows - 1) / _stripRows;

        public void DrawAll()
        {
            var buffers = CreateBuffers();
            for (int strip = 0; strip < StripCount; strip++)
            {
                DrawStrip(strip, null, buffers);
            }

            ReleaseBuffers(buffers);
        }

        // One full-size strip and depth buffer per worker, sized for the tallest strip
        public StripBuffers CreateBuffers()
        {
            int rows = (int)MathF.Ceiling(_stripRows / _scale) + 1;
            return new StripBuffers(ArrayPool<Rgb24>.Shared.Rent(Math.Max(1, _width * rows)), ArrayPool<int>.Shared.Rent(Math.Max(1, _width * rows)),
                _ambient.HasValue ? ArrayPool<Rgb24>.Shared.Rent(Math.Max(1, _width * rows)) : [], ArrayPool<int>.Shared.Rent(3 * _area.Width + 3),
                ArrayPool<Rgb24>.Shared.Rent(_area.Width + 1));
        }

        public void ReleaseBuffers(StripBuffers buffers)
        {
            ArrayPool<Rgb24>.Shared.Return(buffers.Source);
            ArrayPool<int>.Shared.Return(buffers.Depth);
            if (_ambient.HasValue)
            {
                ArrayPool<Rgb24>.Shared.Return(buffers.Light);
            }

            ArrayPool<int>.Shared.Return(buffers.Sums);
            ArrayPool<Rgb24>.Shared.Return(buffers.Row);
        }

        public StripBuffers DrawStrip(int strip, ParallelLoopState? state, StripBuffers buffers)
        {
            int out0 = _area.Top + strip * _stripRows;
            int out1 = Math.Min(_area.Bottom, out0 + _stripRows);
            int y0 = GetSourceStart(out0, _height);
            int y1 = GetSourceStart(out1, _height);
            (var source, int[] depth, var light, _, _) = buffers;
            if (y1 > y0 && _width > 0)
            {
                source.AsSpan(0, _width * (y1 - y0)).Fill(_background);
                Array.Clear(depth, 0, _width * (y1 - y0));
                bool lighting = _ambient.HasValue;
                bool started = false;
                foreach (int index in _strips[strip])
                {
                    var p = _placed[index];
                    if (lighting && p.Pass >= LightPass && !started)
                    {
                        StartLight(light, depth, y0, y1);
                        started = true;
                    }

                    if (lighting && p.Pass > LightPass)
                    {
                        ApplyLight(source, light, y0, y1);
                        lighting = false;
                    }

                    var frame = _states[index].Frame!;
                    int top = _states[index].Y + frame.Top;
                    if (top < y1 && top + frame.Rows > y0)
                    {
                        Draw(started && lighting ? light : source, p, _states[index], y0, y1, depth);
                    }
                }

                if (lighting)
                {
                    if (!started)
                    {
                        StartLight(light, depth, y0, y1);
                    }

                    ApplyLight(source, light, y0, y1);
                }
            }

            for (int output = out0; output < out1; output++)
            {
                WriteRow(output, buffers, y0, y1);
            }

            return buffers;
        }

        private void AddToStrips(int index)
        {
            var frame = _states[index].Frame;
            if (frame is null || frame.Rows == 0)
            {
                return;
            }

            int x = _states[index].X;
            int top = _states[index].Y + frame.Top;
            if (x >= _left + _width || x + frame.Width <= _left)
            {
                return;
            }

            int first = (int)MathF.Floor(top * _scale) - 1 - _area.Top;
            int last = (int)MathF.Floor((top + frame.Rows) * _scale) + 1 - _area.Top;
            if (last < 0 || first >= _area.Height)
            {
                return;
            }

            for (int strip = Math.Max(0, first) / _stripRows; strip <= Math.Min(_area.Height - 1, last) / _stripRows; strip++)
            {
                _strips[strip].Add(index);
            }
        }

        private void WriteRow(int output, StripBuffers buffers, int y0, int y1)
        {
            var row = buffers.Row.AsSpan(0, _area.Width);
            int first = Math.Max(y0, GetSourceStart(output, _height)) - y0;
            int last = Math.Min(y1, GetSourceStart(output + 1, _height)) - y0;
            if (_scale == 1 && last - first == 1 && _width == _area.Width)
            {
                buffers.Source.AsSpan(first * _width, _width).CopyTo(row);
                _writer(output - _area.Top, row);
                return;
            }

            var sums = buffers.Sums.AsSpan(0, 3 * _area.Width);
            sums.Clear();
            for (int sourceRow = first; sourceRow < last; sourceRow++)
            {
                var pixels = buffers.Source.AsSpan(sourceRow * _width, _width);
                for (int column = 0; column < _area.Width; column++)
                {
                    for (int x = _columnStarts[column]; x < _columnStarts[column + 1]; x++)
                    {
                        sums[3 * column] += pixels[x].R;
                        sums[3 * column + 1] += pixels[x].G;
                        sums[3 * column + 2] += pixels[x].B;
                    }
                }
            }

            for (int column = 0; column < _area.Width; column++)
            {
                int count = (last - first) * (_columnStarts[column + 1] - _columnStarts[column]);
                row[column] = count <= 0 ? _background
                    : new Rgb24((byte)((sums[3 * column] + count / 2) / count), (byte)((sums[3 * column + 1] + count / 2) / count),
                        (byte)((sums[3 * column + 2] + count / 2) / count));
            }

            _writer(output - _area.Top, row);
        }

        // First source pixel of an output pixel, clamped to the size of the scene
        private int GetSourceStart(int output, int limit) => Math.Min(limit, (int)MathF.Ceiling(output / _scale - 1e-9f));

        // Engine depth is 8 per unit of height, plus 0x400 for flat sprites and 8 more per row up for standing ones
        private void Draw(Rgb24[] target, Placement p, PlacementState state, int y0, int y1, int[] depth)
        {
            var frame = state.Frame!;
            int baseDepth = state.BaseDepth;
            int flat = Math.Min(baseDepth + FlatDepth, MaxFlatDepth);
            bool standing = p.IsStanding;
            (short[] xs, byte[] indices, ushort[] depths, var colors) = (frame.Xs, frame.Indices, frame.Depths, frame.Colors);
            bool hasDepths = depths.Length > 0;
            int firstRow = Math.Clamp(y0 - state.Y - frame.Top, 0, frame.Rows);
            int lastRow = Math.Clamp(y1 - state.Y - frame.Top, 0, frame.Rows);
            int left = state.X - _left;
            for (int row = firstRow; row < lastRow; row++)
            {
                int y = state.Y + frame.Top + row;
                int depthRow = (y - y0) * _width;
                var pixels = target.AsSpan(depthRow, _width);
                int rowDepth = standing ? flat + (frame.Rows - row) * 8 : flat;
                for (int index = frame.RowStarts[row]; index < frame.RowStarts[row + 1]; index++)
                {
                    int x = left + xs[index];
                    if ((uint)x >= (uint)_width)
                    {
                        continue;
                    }

                    int d = hasDepths ? baseDepth + depths[index] : rowDepth;
                    int k = depthRow + x;
                    int ci = frame.IsPaletted ? indices[index] : index;
                    if (frame.IsCombined)
                    {
                        if (!frame.TestsDepth || depth[k] <= d)
                        {
                            ref var under = ref pixels[x];
                            under = Combine(frame.Blend, colors[ci], under);
                        }
                    }
                    else if (frame.IsBlended)
                    {
                        if (!frame.TestsDepth || depth[k] <= d)
                        {
                            var color = colors[ci];
                            float a = color.A / 255f;
                            ref var under = ref pixels[x];
                            under = new Rgb24(Blend(color.R, under.R, a), Blend(color.G, under.G, a), Blend(color.B, under.B, a));
                        }
                    }
                    else if (!frame.TestsDepth || (!hasDepths ? depth[k] <= d : depth[k] < d))
                    {
                        if (frame.IsPaletted || colors[ci].A > 0)
                        {
                            pixels[x] = colors[ci].Rgb;
                            if (frame.WritesDepth)
                            {
                                depth[k] = d;
                            }
                        }
                    }
                }
            }
        }

        private void StartLight(Rgb24[] light, int[] depth, int y0, int y1)
        {
            var ambient = _ambient!.Value;
            light.AsSpan(0, (y1 - y0) * _width).Fill(new Rgb24(GetBufferAmbient(ambient, 16), GetBufferAmbient(ambient, 8), GetBufferAmbient(ambient, 0)));
            for (int lightIndex = 0; lightIndex < _lights.Length; lightIndex++)
            {
                AddLight(light, depth, _lights[lightIndex], _colors[lightIndex], y0, y1);
            }
        }

        private void AddLight(Rgb24[] buffer, int[] depth, OmniLight light, Vector3 color, int y0, int y1)
        {
            float centerY = light.Position.Y - light.Position.Z + LightQuadDrop;
            int top = Math.Max(y0, (int)MathF.Floor(centerY - light.RadiusY));
            int bottom = Math.Min(y1, (int)MathF.Ceiling(centerY + light.RadiusY));
            int left = Math.Max(0, (int)MathF.Floor(light.Position.X - light.RadiusX));
            int right = Math.Min(_width, (int)MathF.Ceiling(light.Position.X + light.RadiusX));
            float scale = GetLightScale(light.RadiusY);
            var scaled = color * scale;
            (int red, int green, int blue) = ((int)scaled.X, (int)scaled.Y, (int)scaled.Z);
            int strongest = Math.Max(red, Math.Max(green, blue));
            if (top >= bottom || left >= right || strongest == 0)
            {
                return;
            }

            bool[] shadowed = GetShadowedPixels(light, depth, left, top, right, bottom, y0);
            for (int y = top; y < bottom; y++)
            {
                float v = (y + 0.5f - centerY) / light.RadiusY;
                for (int x = left; x < right; x++)
                {
                    if (shadowed.Length > 0 && shadowed[(y - top) * (right - left) + x - left])
                    {
                        continue;
                    }

                    int falloff = GetFalloff((x + 0.5f - light.Position.X) / light.RadiusX, v);
                    if (falloff * strongest <= LightAlphaReference * 255)
                    {
                        continue;
                    }

                    ref var pixel = ref buffer[(y - y0) * _width + x];
                    pixel = new Rgb24((byte)Math.Min(255, pixel.R + falloff * red / 255), (byte)Math.Min(255, pixel.G + falloff * green / 255),
                        (byte)Math.Min(255, pixel.B + falloff * blue / 255));
                }
            }
        }

        private void ApplyLight(Rgb24[] target, Rgb24[] light, int y0, int y1)
        {
            var ambient = _ambient!.Value;
            (int red, int green, int blue) = (ambient.GetPositive(16), ambient.GetPositive(8), ambient.GetPositive(0));
            int count = (y1 - y0) * _width;
            for (int pixel = 0; pixel < count; pixel++)
            {
                var color = target[pixel];
                var factor = light[pixel];
                target[pixel] = new Rgb24(ApplyLight(color.R, factor.R, red), ApplyLight(color.G, factor.G, green), ApplyLight(color.B, factor.B, blue));
            }
        }

        private bool[] GetShadowedPixels(OmniLight light, int[] depth, int left, int top, int right, int bottom, int y0)
        {
            if (light.Shadows.Length == 0)
            {
                return [];
            }

            int width = right - left;
            bool[] shadowed = new bool[width * (bottom - top)];
            foreach (var card in light.Shadows)
            {
                var columnEnd = card.ColumnStep * card.Columns;
                var rowEnd = card.RowStep * card.Rows;
                var lowest = Vector2.Min(Vector2.Min(card.Origin, card.Origin + columnEnd), Vector2.Min(card.Origin + rowEnd, card.Origin + columnEnd + rowEnd));
                var highest = Vector2.Max(Vector2.Max(card.Origin, card.Origin + columnEnd), Vector2.Max(card.Origin + rowEnd, card.Origin + columnEnd + rowEnd));
                int cardLeft = Math.Max(left, (int)MathF.Floor(lowest.X));
                int cardRight = Math.Min(right, (int)MathF.Ceiling(highest.X));
                int cardTop = Math.Max(top, (int)MathF.Floor(lowest.Y));
                int cardBottom = Math.Min(bottom, (int)MathF.Ceiling(highest.Y));
                if (cardLeft >= cardRight || cardTop >= cardBottom)
                {
                    continue;
                }

                float determinant = Vector2.Cross(card.ColumnStep, card.RowStep);
                if (determinant == 0)
                {
                    continue;
                }

                for (int y = cardTop; y < cardBottom; y++)
                {
                    for (int x = cardLeft; x < cardRight; x++)
                    {
                        var delta = new Vector2(x + 0.5f, y + 0.5f) - card.Origin;
                        float column = Vector2.Cross(delta, card.RowStep) / determinant;
                        float row = Vector2.Cross(card.ColumnStep, delta) / determinant;
                        if (column < 0 || row < 0 || column >= card.Columns || row >= card.Rows || !card.Mask[(int)row * card.Columns + (int)column])
                        {
                            continue;
                        }

                        float cardDepth = card.TopDepth + (card.BottomDepth - card.TopDepth) * row / card.Rows;
                        if (cardDepth >= depth[(y - y0) * _width + x])
                        {
                            shadowed[(y - top) * width + x - left] = true;
                        }
                    }
                }
            }

            return shadowed;
        }

        private static byte GetBufferAmbient(PackedTint ambient, int shift) =>
            (byte)(Math.Min(255, 255 * (256 - ambient.GetNegative(shift)) / 256 + ambient.GetPositive(shift)) * LightBufferShare);

        // Multiply the scene by twice the buffer plus the positive ambient, then double it like the second engine quad
        private static byte ApplyLight(byte color, byte light, int positive) => (byte)Math.Min(255, 2 * Math.Min(255, 2 * color * Math.Min(255, light + positive) / 255));

        private static Rgb24 Combine(VidBlend blend, Rgba32 over, Rgb24 under) => blend switch
        {
            VidBlend.Multiply => new Rgb24(Modulate(over.R, under.R, 2), Modulate(over.G, under.G, 2), Modulate(over.B, under.B, 2)),
            VidBlend.Brighten => new Rgb24((byte)Math.Min(255, Modulate(over.R, under.R, 1) + under.R), (byte)Math.Min(255, Modulate(over.G, under.G, 1) + under.G),
                (byte)Math.Min(255, Modulate(over.B, under.B, 1) + under.B)),
            _ => new Rgb24((byte)Math.Min(255, over.R + under.R), (byte)Math.Min(255, over.G + under.G), (byte)Math.Min(255, over.B + under.B))
        };

        private static byte Modulate(byte a, byte b, int factor) => (byte)Math.Min(255, factor * a * b / 255);

        private static byte Blend(byte over, byte under, float alpha) => (byte)Math.Clamp(over * alpha + under * (1 - alpha), 0, 255);
    }
}

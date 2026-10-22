using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Renders maps the way the game engines draw them, showing the first animation frame of each object.
/// </summary>
public sealed class MapRenderer
{
    private const int Margin = 4;
    private const int StripRows = 16;
    private const int MaxStripWorkers = 8;
    private const int PoolMegabytes = 32;
    private const int MissingSize = 8;
    private const int FlatDepth = 0x400;
    private const int MaxFlatDepth = 0x7fff;
    private const int DepthPerHeight = 8;
    private const double MeshTolerance = 0.01;
    private const int RaisedDepth = 0x3fff;
    private const int SequelRaisedHeight = 1000;
    private const int MaxAttachDepth = 4;
    private const uint PlayerSide = 0;
    private const uint SideMask = 3;
    private const uint SequelSideMask = 7;
    private const int LightPass = 7;
    private const float LightStrength = 0.2f;
    private const float LightBias = 0.2f;
    private const float MinLightHeight = 2;
    private const float LightDepthReach = 150;
    private const float OctagonalMajor = 0.961f;
    private const float OctagonalMinor = 0.398f;
    private const double AngleUnitsPerRadian = 40.74366760253906;
    private const int HalfTurn = 0x80;
    private const float FlatShadowRatio = 1.8f;
    private const float FlatShadowPush = 0.6f;
    private const float TopShadowHeight = 0.3f;
    private const float InitialLowestHeight = 999;
    private const int ActionStackCount = 4;
    private const int PauseAction = 40;
    private const int TriggerAction = 43;
    private const int StopAction = 73;
    private const int ScriptCheckAction = 77;
    private const int VariableWaitAction = 79;
    private const int HideAction = 98;
    private const float LightHeightRange = 512;
    private const double RowScale = 0.70706;
    private const uint SceneryType = 8;
    private const uint InterfaceType = 10;
    private const uint FontType = 19;
    private const uint RegionType = 23;
    
    private static readonly MemoryAllocator Allocator = MemoryAllocator.Create(new MemoryAllocatorOptions { MaximumPoolSizeMegabytes = PoolMegabytes });
    private static readonly Configuration ImageConfiguration = CreateConfiguration(Environment.ProcessorCount);
    private static readonly Configuration StripConfiguration = CreateConfiguration(1);

    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ConcurrentDictionary<string, VidResource?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<FrameKey, SpriteFrame?> _frames = new();
    private readonly ConcurrentDictionary<(string Path, PackedTint Tint, bool TintsAlpha), Rgba32[]> _palettes = new();
    private readonly ConcurrentDictionary<(string Path, int Frame), bool[]> _masks = new();

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
    /// Gets a value indicating whether the Alien Shooter 2 sprites that hide themselves when the level starts are left out.
    /// </summary>
    public bool OmitHiddenObjects { get; init; } = true;

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
    public MapImage Render(IReadOnlyList<MergedMapSprite> sprites, double scale = 1, MapGraphics graphics = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 1);
        var scene = new SceneObjects(new List<Placement>(sprites.Count), [], []);
        var ambient = MatchGameLighting && _objects.IsAlienShooter2 ? GetAmbient(graphics) : (PackedTint?)null;
        int undrawn = 0;
        foreach (var sprite in sprites)
        {
            if (!(OmitRemovableGroups && IsRemovableGroup(sprite.Payload)) && !(OmitCovers && IsCover(sprite.Id))
                && !(OmitHiddenObjects && _objects.IsAlienShooter2 && StartsHidden(sprite.Payload)))
            {
                var look = new SpriteLook(GetPlacementTint(sprite), new Vector3(sprite.ScaleX, sprite.ScaleY, sprite.ScaleZ));
                undrawn += Place(scene, sprite.Id, sprite.X, sprite.Y, sprite.Z, sprite.Facing, sprite.Side, look, ambient, 0);
            }
        }

        var placed = scene.Placed;
        var lights = scene.Lights.Select(light => light.CastsShadows ? light with { Shadows = GetShadows(light, scene.Casters) } : light).ToList();
        if (placed.Count == 0)
        {
            return new MapImage { Image = new Image<Rgb24>(ImageConfiguration, 640, 360, Background), OriginX = 0, OriginY = 0, Scale = scale, Undrawn = undrawn };
        }

        int x0 = (int)placed.Min(p => p.Left) - Margin;
        int y0 = (int)placed.Min(p => p.Top) - Margin;
        int width = (int)placed.Max(p => p.Left + p.Width) - x0 + Margin;
        int height = (int)placed.Max(p => p.Top + p.Height) - y0 + Margin;
        var image = new Image<Rgb24>(ImageConfiguration, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), Background);
        var canvas = new Canvas(placed, lights, x0, y0, width, height, scale, image, ambient);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaxStripWorkers) };
        Parallel.For(0, canvas.StripCount, options, canvas.CreateBuffers, canvas.DrawStrip, canvas.ReleaseBuffers);
        return new MapImage { Image = image, OriginX = x0, OriginY = y0, Scale = scale, Undrawn = undrawn };
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
            if (action.Code is PauseAction or TriggerAction or StopAction or ScriptCheckAction or VariableWaitAction)
            {
                break;
            }

            if (action.Code == HideAction)
            {
                hidden = action.First != 0;
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

    private int Place(SceneObjects scene, KnownObject id, float x, float y, float z, byte dir, uint side, SpriteLook look, PackedTint? ambient,
        int attachDepth)
    {
        if (!_objects.TryFind(id, out var obj) || (obj.Flags.HasFlag(ObjectFlags.SideDependent) && GetSideBits(side) != PlayerSide))
        {
            return 0;
        }

        string path = _objects.GetPath(id);
        var file = GetFile(path);
        int frameIndex = file is null ? 0 : obj.GetFrame(dir, file.Value.FrameCount);
        bool sequel = _objects.IsAlienShooter2;
        bool isOmniLight = sequel && file is not null && file.Value.Flags.HasFlag(VidFlags.OmniLight);
        int sequelPass = isOmniLight ? LightPass : sequel && file is not null ? GetPass(obj, file.Value) : 0;
        var objectTint = PackedTint.Pack(obj.TintRed, obj.TintGreen, obj.TintBlue, obj.TintAlpha);
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
        if (isOmniLight && ambient.HasValue)
        {
            AddLight(scene.Lights, obj, file!.Value.GetLightColor(frameIndex), tint, x, y, z, look.Scale);
        }

        if (ambient.HasValue && sequel && file is not null && IsShadowCaster(obj, file.Value))
        {
            scene.Casters.Add(new ShadowCaster(obj, path, file.Value, x, y, z, dir, frameIndex, scale));
        }

        var frame = file is null ? null : GetFrame(path, obj, file.Value, frameIndex, GetRotation(obj, file.Value, path, dir), tint, scale);
        (int width, int height) = frame is not null ? (frame.Width, frame.Height) : file is null ? (MissingSize, MissingSize) : (file.Value.Width, file.Value.Height);
        int pass = sequel && file is not null ? sequelPass : frame is null || !frame.IsCombined ? 0 : 1;
        scene.Placed.Add(new Placement(obj, frame, x - width / 2.0, y - z - height / 2.0, width, height, GetBaseDepth(obj, z, sequel), !sequel && obj.ExtentY < obj.ExtentZ, pass));
        int undrawn = file is not null && frame is null ? 1 : 0;
        if (obj.AttachedObject == 0 || attachDepth >= MaxAttachDepth || !_objects.TryFind(obj.AttachedObject, out var attached))
        {
            return undrawn;
        }

        var offset = GetAttachedOffset(obj, attached, file, frameIndex, dir);
        return undrawn + Place(scene, obj.AttachedObject, x + offset.X, y + offset.Y, z + offset.Z, dir, side, SpriteLook.Default, ambient, attachDepth + 1);
    }

    private uint GetSideBits(uint side) => side & (_objects.IsAlienShooter2 ? SequelSideMask : SideMask);

    private static Vector3 GetAttachedOffset(GameObject owner, GameObject attached, VidResource? file, int frameIndex, byte dir)
    {
        var offset = file is not null && file.Value.Flags.HasFlag(VidFlags.FrameVector)
            ? file.Value.GetFrameVector(frameIndex)
            : new Vector3(owner.AttachedOffsetX, -owner.AttachedOffsetY, owner.AttachedOffsetZ);
        if (attached.Flags.HasFlag(ObjectFlags.FixedOffset))
        {
            return offset;
        }

        (double sin, double cos) = Math.SinCos(dir * Math.PI / 128);
        return new Vector3((float)(offset.X * cos - offset.Y * sin), (float)(offset.X * sin + offset.Y * cos), offset.Z);
    }

    private static int GetBaseDepth(GameObject obj, float z, bool sequel)
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
        if (mode == 0 || (mode == -1 && obj.ExtentY < obj.ExtentZ))
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

        if (obj.Type == SceneryType)
        {
            return file is { IsBlended: false, Blend: VidBlend.Alpha } ? 0 : 2;
        }

        if (obj.Type == RegionType)
        {
            return 5;
        }

        if (obj.Type is InterfaceType or FontType)
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

    private static void AddLight(List<OmniLight> lights, GameObject obj, uint color, PackedTint tint, float x, float y, float z, Vector3 scale)
    {
        float radiusX = 0.5f * obj.ExtentX * obj.ScaleX * scale.X;
        float radiusY = 0.5f * obj.ExtentY * obj.ScaleY * scale.Y;
        if (color == 0 || radiusX <= 0 || radiusY <= 0)
        {
            return;
        }

        var lightColor = new Vector3(GetLightChannel(tint.Apply((int)(color >> 16 & 0xff), 16)), GetLightChannel(tint.Apply((int)(color >> 8 & 0xff), 8)),
            GetLightChannel(tint.Apply((int)(color & 0xff), 0)));
        lights.Add(new OmniLight(x, y, z, radiusX, radiusY, lightColor, obj.Flags.HasFlag(ObjectFlags.Windblown), []));
    }

    private static float GetLightChannel(int value) => (float)(value / 255.0 * LightStrength);

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
            var offset = new Vector3(caster.X - light.X, caster.Y - light.Y, caster.Z - light.Height);
            if (GetOctagonalLength(GetOctagonalLength(Math.Abs(offset.X), Math.Abs(offset.Y)), Math.Abs(offset.Z)) < light.RadiusX)
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
        float towardLight = MathF.Atan2(light.X - caster.X, -(light.Y - caster.Y));
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

        float lift = Math.Max(0, caster.Z);
        var scale = caster.Scale;
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

            return new Vector2(caster.X + local.X, (caster.Y - caster.Z) + local.Y + lift);
        }

        var origin = ToScreen(0, 0);
        var columnStep = (ToScreen(frame.Width, 0) - origin) / frame.Width;
        var rowStep = (ToScreen(0, frame.RowCount) - origin) / frame.RowCount;
        float baseDepth = DepthPerHeight * caster.Z + FlatDepth;
        return new ShadowCard(GetMask(caster.Path, file, index), frame.Width, frame.RowCount, origin, columnStep, rowStep,
            DepthPerHeight * highest * TopShadowHeight * scale.Z + baseDepth, DepthPerHeight * lowest * scale.Z + baseDepth);
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

    private static float[] RasterizeHeights(VidMesh mesh, int width, int height)
    {
        float[] heights = new float[width * height];
        Array.Fill(heights, float.NaN);
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            var (a, b, c) = mesh.GetTriangle(triangle);
            double area = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
            if (Math.Abs(area) < 1e-9)
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
                    double px = x + 0.5;
                    double py = y + 0.5;
                    double weightA = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / area;
                    double weightB = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / area;
                    double weightC = 1 - weightA - weightB;
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
        (double sin, double cos) = Math.SinCos(rotation * Math.PI / 128);
        int sourceWidth = source.Width;
        int sourceHeight = source.Height;
        double sourceCenterX = sourceWidth / 2.0;
        double sourceCenterY = sourceHeight / 2.0;
        int count = source.Xs.Length;
        int[] lookup = new int[sourceWidth * sourceHeight];
        Array.Fill(lookup, -1);
        float[] heights = new float[count];
        double extentX = 1;
        double extentY = 1;
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
                (double turnedX, double turnedY) = TurnPoint(x + 0.5 - sourceCenterX, y + 0.5 - sourceCenterY, heights[index], sin, cos, scale);
                extentX = Math.Max(extentX, Math.Abs(turnedX));
                extentY = Math.Max(extentY, Math.Abs(turnedY));
            }
        }

        int width = 2 * (int)Math.Ceiling(extentX) + 2;
        int height = 2 * (int)Math.Ceiling(extentY) + 2;
        double centerX = width / 2.0;
        double centerY = height / 2.0;
        int[] target = new int[width * height];
        Array.Fill(target, -1);
        for (int y = 0; y < height; y++)
        {
            double groundY = (y + 0.5 - centerY) / RowScale;
            for (int x = 0; x < width; x++)
            {
                double turnedX = x + 0.5 - centerX;
                int sourceX = (int)Math.Floor((turnedX * cos + groundY * sin) / scale.X + sourceCenterX);
                int sourceY = (int)Math.Floor(RowScale * (groundY * cos - turnedX * sin) / scale.Y + sourceCenterY);
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

    private static (double X, double Y) TurnPoint(double x, double y, double height, double sin, double cos, Vector3 scale)
    {
        double groundX = x * scale.X;
        double groundY = (y + height) / RowScale * scale.Y;
        return (groundX * cos - groundY * sin, RowScale * (groundX * sin + groundY * cos) - height * scale.Z);
    }

    private static void PlaceTriangle(VidVertex[] vertices, double sin, double cos, Vector3 scale, double sourceCenterX, double sourceCenterY, int sourceWidth, int sourceHeight,
        double centerX, double centerY, int width, int height, int[] lookup, int[] target, float[] targetHeights)
    {
        var points = new (double X, double Y)[3];
        for (int vertex = 0; vertex < 3; vertex++)
        {
            (double turnedX, double turnedY) = TurnPoint(vertices[vertex].X - sourceCenterX, vertices[vertex].Y - sourceCenterY, vertices[vertex].Z, sin, cos, scale);
            points[vertex] = (turnedX + centerX, turnedY + centerY);
        }

        double area = (points[1].Y - points[2].Y) * (points[0].X - points[2].X) + (points[2].X - points[1].X) * (points[0].Y - points[2].Y);
        if (Math.Abs(area) < 1e-9)
        {
            return;
        }

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(points[0].X, Math.Min(points[1].X, points[2].X))));
        int maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(points[0].X, Math.Max(points[1].X, points[2].X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(points[0].Y, Math.Min(points[1].Y, points[2].Y))));
        int maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(points[0].Y, Math.Max(points[1].Y, points[2].Y))));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                double weightA = ((points[1].Y - points[2].Y) * (px - points[2].X) + (points[2].X - points[1].X) * (py - points[2].Y)) / area;
                double weightB = ((points[2].Y - points[0].Y) * (px - points[2].X) + (points[0].X - points[2].X) * (py - points[2].Y)) / area;
                double weightC = 1 - weightA - weightB;
                if (weightA < -MeshTolerance || weightB < -MeshTolerance || weightC < -MeshTolerance)
                {
                    continue;
                }

                float z = (float)(weightA * vertices[0].Z + weightB * vertices[1].Z + weightC * vertices[2].Z);
                int sourceX = (int)Math.Floor(weightA * vertices[0].X + weightB * vertices[1].X + weightC * vertices[2].X);
                int sourceY = (int)Math.Floor(weightA * vertices[0].Y + weightB * vertices[1].Y + weightC * vertices[2].Y);
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

    private readonly record struct FrameKey(string Path, PackedTint Tint, int Frame, byte Rotation, Vector3 Scale);

    private readonly record struct SpriteLook(PackedTint? Tint, Vector3 Scale)
    {
        public static SpriteLook Default { get; } = new(null, Vector3.One);
    }

    private readonly record struct PackedTint(uint Negative, uint Positive)
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

    private sealed record OmniLight(float X, float Y, float Height, float RadiusX, float RadiusY, Vector3 Color, bool CastsShadows, ShadowCard[] Shadows);

    private sealed record ShadowCaster(GameObject Object, string Path, VidResource File, float X, float Y, float Z, byte Dir, int FrameIndex, Vector3 Scale);

    private sealed record ShadowCard(bool[] Mask, int Columns, int Rows, Vector2 Origin, Vector2 ColumnStep, Vector2 RowStep, float TopDepth, float BottomDepth);

    private sealed record SceneObjects(List<Placement> Placed, List<OmniLight> Lights, List<ShadowCaster> Casters);

    private sealed record StripBuffers(Image<Rgb24> Source, int[] Depth, int[] Light);

    private sealed record Placement(GameObject Object, SpriteFrame? Frame, double Left, double Top, int Width, int Height, int BaseDepth, bool IsStanding, int Pass);

    // Draw strips of output rows in parallel, each at full size with its own depth buffer, then scale them into the image
    private sealed class Canvas
    {
        private readonly List<Placement> _placed;
        private readonly (int X, int Y)[] _origins;
        private readonly int[] _order;
        private readonly int _width;
        private readonly int _height;
        private readonly double _scale;
        private readonly Image<Rgb24> _image;
        private readonly int _stripRows;
        private readonly PackedTint? _ambient;
        private readonly OmniLight[] _lights;

        public Canvas(List<Placement> placed, IReadOnlyList<OmniLight> lights, int x0, int y0, int width, int height, double scale, Image<Rgb24> image, PackedTint? ambient)
        {
            _placed = placed;
            _origins = placed.Select(p => ((int)Math.Round(p.Left - x0), (int)Math.Round(p.Top - y0))).ToArray();
            _order = Enumerable.Range(0, placed.Count).OrderBy(index => placed[index].Pass).ToArray();
            _width = width;
            _height = height;
            _scale = scale;
            _image = image;
            _stripRows = Math.Max(1, (int)(StripRows * scale));
            _ambient = ambient;
            _lights = lights.Select(light => light with { X = light.X - x0, Y = light.Y - y0, Shadows = light.Shadows.Select(card => card with { Origin = card.Origin - new Vector2(x0, y0) }).ToArray() }).ToArray();
        }

        public int StripCount => (_image.Height + _stripRows - 1) / _stripRows;

        // One full-size strip image and depth buffer per worker, sized for the tallest strip
        public StripBuffers CreateBuffers()
        {
            int rows = (int)Math.Ceiling(_stripRows / _scale) + 1;
            return new StripBuffers(new Image<Rgb24>(StripConfiguration, _width, rows), ArrayPool<int>.Shared.Rent(_width * rows),
                _ambient.HasValue ? ArrayPool<int>.Shared.Rent(3 * _width * rows) : []);
        }

        public void ReleaseBuffers(StripBuffers buffers)
        {
            buffers.Source.Dispose();
            ArrayPool<int>.Shared.Return(buffers.Depth);
            if (_ambient.HasValue)
            {
                ArrayPool<int>.Shared.Return(buffers.Light);
            }
        }

        public StripBuffers DrawStrip(int strip, ParallelLoopState state, StripBuffers buffers)
        {
            int out0 = strip * _stripRows;
            int out1 = Math.Min(_image.Height, out0 + _stripRows);
            int y0 = GetSourceStart(out0);
            int y1 = GetSourceStart(out1);
            if (y1 <= y0)
            {
                return buffers;
            }

            (var source, int[] depth, int[] light) = buffers;
            for (int row = 0; row < y1 - y0; row++)
            {
                source.DangerousGetPixelRowMemory(row).Span.Fill(Background);
            }

            Array.Clear(depth, 0, _width * (y1 - y0));
            bool lit = !_ambient.HasValue;
            foreach (int placedObjectIndex in _order)
            {
                var p = _placed[placedObjectIndex];
                if (!lit && p.Pass >= LightPass)
                {
                    LightScene(source, depth, light, y0, y1);
                    lit = true;
                }

                var frame = p.Frame;
                if (frame is not null && _origins[placedObjectIndex].Y + frame.Top < y1 && _origins[placedObjectIndex].Y + frame.Top + frame.Rows > y0)
                {
                    Draw(source, p, frame, _origins[placedObjectIndex], y0, y1, depth);
                }
            }

            if (!lit)
            {
                LightScene(source, depth, light, y0, y1);
            }

            if (_scale < 1)
            {
                using var scaled = source.Clone(StripConfiguration, c => c.Crop(new Rectangle(0, 0, _width, y1 - y0)).Resize(_image.Width, out1 - out0, KnownResamplers.Box));
                CopyRows(scaled, out0);
            }
            else
            {
                CopyRows(source, out0, out1 - out0);
            }

            return buffers;
        }

        private void CopyRows(Image<Rgb24> strip, int out0, int? rows = null)
        {
            for (int row = 0; row < (rows ?? strip.Height); row++)
            {
                strip.DangerousGetPixelRowMemory(row).Span.CopyTo(_image.DangerousGetPixelRowMemory(out0 + row).Span);
            }
        }

        // First source row of an output row, clamped to the source height
        private int GetSourceStart(int output) => Math.Min(_height, (int)Math.Ceiling(output / _scale - 1e-9));

        // Engine depth is 8 per unit of height, plus 0x400 for flat sprites and 8 more per row up for standing ones
        private void Draw(Image<Rgb24> target, Placement p, SpriteFrame frame, (int X, int Y) origin, int y0, int y1, int[] depth)
        {
            int baseDepth = p.BaseDepth;
            int flat = Math.Min(baseDepth + FlatDepth, MaxFlatDepth);
            bool standing = p.IsStanding;
            (short[] xs, byte[] indices, ushort[] depths, var colors) = (frame.Xs, frame.Indices, frame.Depths, frame.Colors);
            bool hasDepths = depths.Length > 0;
            int firstRow = Math.Clamp(y0 - origin.Y - frame.Top, 0, frame.Rows);
            int lastRow = Math.Clamp(y1 - origin.Y - frame.Top, 0, frame.Rows);
            for (int row = firstRow; row < lastRow; row++)
            {
                int y = origin.Y + frame.Top + row;
                var pixels = target.DangerousGetPixelRowMemory(y - y0).Span;
                int depthRow = (y - y0) * _width;
                int rowDepth = standing ? flat + (frame.Rows - row) * 8 : flat;
                for (int index = frame.RowStarts[row]; index < frame.RowStarts[row + 1]; index++)
                {
                    int x = origin.X + xs[index];
                    if ((uint)x >= (uint)_width)
                    {
                        continue;
                    }

                    int d = hasDepths ? baseDepth + depths[index] : rowDepth;
                    int k = depthRow + x;
                    int ci = frame.IsPaletted ? indices[index] : index;
                    if (frame.IsCombined)
                    {
                        if (depth[k] <= d)
                        {
                            ref var under = ref pixels[x];
                            under = Combine(frame.Blend, colors[ci], under);
                        }
                    }
                    else if (frame.IsBlended)
                    {
                        if (depth[k] <= d)
                        {
                            var color = colors[ci];
                            float a = color.A / 255f;
                            ref var under = ref pixels[x];
                            under = new Rgb24(Blend(color.R, under.R, a), Blend(color.G, under.G, a), Blend(color.B, under.B, a));
                        }
                    }
                    else if (!hasDepths ? depth[k] <= d : depth[k] < d)
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

        private void LightScene(Image<Rgb24> target, int[] depth, int[] sum, int y0, int y1)
        {
            var ambient = _ambient!.Value;
            int rows = y1 - y0;
            for (int row = 0; row < rows; row++)
            {
                var pixels = target.DangerousGetPixelRowMemory(row).Span;
                for (int x = 0; x < _width; x++)
                {
                    int offset = 3 * (row * _width + x);
                    var color = pixels[x];
                    sum[offset] = GetAmbientColor(color.R, ambient, 16);
                    sum[offset + 1] = GetAmbientColor(color.G, ambient, 8);
                    sum[offset + 2] = GetAmbientColor(color.B, ambient, 0);
                }
            }

            foreach (var light in _lights)
            {
                float centerY = light.Y - light.Height;
                int top = Math.Max(y0, (int)Math.Floor(centerY - light.RadiusY));
                int bottom = Math.Min(y1, (int)Math.Ceiling(centerY + light.RadiusY));
                int left = Math.Max(0, (int)Math.Floor(light.X - light.RadiusX));
                int right = Math.Min(_width, (int)Math.Ceiling(light.X + light.RadiusX));
                if (top >= bottom || left >= right)
                {
                    continue;
                }

                bool[] shadowed = GetShadowedPixels(light, depth, left, top, right, bottom, y0);
                int lightDepth = (int)(DepthPerHeight * (light.Height + LightDepthReach)) + FlatDepth;
                for (int y = top; y < bottom; y++)
                {
                    var pixels = target.DangerousGetPixelRowMemory(y - y0).Span;
                    float groundY = (float)((y + 0.5 - centerY) / RowScale - light.Height);
                    for (int x = left; x < right; x++)
                    {
                        int k = (y - y0) * _width + x;
                        if (depth[k] > lightDepth || (shadowed.Length > 0 && shadowed[(y - top) * (right - left) + x - left]))
                        {
                            continue;
                        }

                        float height = GetSceneHeight(depth[k]);
                        float cx = (x + 0.5f - light.X) / light.RadiusX;
                        float cy = (groundY + height) / light.RadiusY;
                        float cz = (light.Height - height) / light.RadiusY;
                        float intensity = 1 / (cx * cx + cy * cy + cz * cz + LightBias * LightBias) - 1;
                        if (intensity <= 0)
                        {
                            continue;
                        }

                        var color = pixels[x];
                        int offset = 3 * k;
                        sum[offset] += (int)Math.Min(255, color.R * light.Color.X * intensity);
                        sum[offset + 1] += (int)Math.Min(255, color.G * light.Color.Y * intensity);
                        sum[offset + 2] += (int)Math.Min(255, color.B * light.Color.Z * intensity);
                    }
                }
            }

            for (int row = 0; row < rows; row++)
            {
                var pixels = target.DangerousGetPixelRowMemory(row).Span;
                for (int x = 0; x < _width; x++)
                {
                    int offset = 3 * (row * _width + x);
                    pixels[x] = new Rgb24((byte)Math.Min(255, sum[offset]), (byte)Math.Min(255, sum[offset + 1]), (byte)Math.Min(255, sum[offset + 2]));
                }
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
                var corners = new[] { card.Origin, card.Origin + columnEnd, card.Origin + rowEnd, card.Origin + columnEnd + rowEnd };
                int cardLeft = Math.Max(left, (int)Math.Floor(corners.Min(corner => corner.X)));
                int cardRight = Math.Min(right, (int)Math.Ceiling(corners.Max(corner => corner.X)));
                int cardTop = Math.Max(top, (int)Math.Floor(corners.Min(corner => corner.Y)));
                int cardBottom = Math.Min(bottom, (int)Math.Ceiling(corners.Max(corner => corner.Y)));
                float determinant = card.ColumnStep.X * card.RowStep.Y - card.ColumnStep.Y * card.RowStep.X;
                if (determinant == 0)
                {
                    continue;
                }

                for (int y = cardTop; y < cardBottom; y++)
                {
                    for (int x = cardLeft; x < cardRight; x++)
                    {
                        var delta = new Vector2(x + 0.5f, y + 0.5f) - card.Origin;
                        float column = (delta.X * card.RowStep.Y - delta.Y * card.RowStep.X) / determinant;
                        float row = (card.ColumnStep.X * delta.Y - card.ColumnStep.Y * delta.X) / determinant;
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

        private static int GetAmbientColor(byte color, PackedTint ambient, int shift) =>
            color * (255 - ambient.GetNegative(shift)) / 255 + ambient.GetPositive(shift);

        private static float GetSceneHeight(int depth)
        {
            float height = Math.Clamp((depth - FlatDepth) / (float)DepthPerHeight, MinLightHeight, LightHeightRange);
            return MathF.Round(height / LightHeightRange * 255) / 255 * LightHeightRange;
        }

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

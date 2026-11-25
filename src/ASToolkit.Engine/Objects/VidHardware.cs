using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the shared data and frames of an object ID whose sprites are drawn from textures, loaded either from the surfaces of a sprite file or from the prerendered ground that built terrain sprites are drawn into.
/// </summary>
public sealed class VidHardware : Vid
{
    /// <summary>
    /// Represents the sprite file flags of the built hardware ground.
    /// </summary>
    public const VidType GroundType = VidType.Runs8Bit | VidType.ZBuffer | VidType.Hardware | VidType.FrameVector;
    private const int TileSize = 256;
    private const ushort EmptyDepth = 0x400;
    private const int DepthBias = 0x400;
    private const float DepthUnit = 0.00012207031f;
    private const float DepthBase = 0.015625f;
    private const float FrontDepth = 0.9999999f;
    private const int WaveTimeShift = 3;
    private const int WaveTableMask = 0xff;
    private const VidType BlendedRuns = VidType.Blended | VidType.Runs8Bit;
    private const int PaletteBytes = 0x300;
    private const int StreamBufferSize = 0x20008;
    private const int LowBlueBits = 3;
    private const int BlueMask = 0x1f;
    private const int HighChannelMask = 0xf8;
    private const int Rgb555Mask = 0x7fe0;

    private static readonly Vector3 GroundObjectSize = new(256, 256, 1);
    private readonly List<HardwareTile> _tiles = [];
    private TextureData[] _surfaces = [];
    private readonly HighColorFormat _format;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidHardware"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    /// <param name="format">The layout of the 16-bit colors that palette surfaces are converted to.</param>
    public VidHardware(KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, in VidResource file, int totalFrames, HighColorFormat format)
        : base(id, gameObject, weapons, file, totalFrames)
    {
        _format = format;
    }

    [EngineRoutine(0x004196a0)]
    private VidHardware(VidHardware source)
        : base(source)
    {
        _format = source._format;
        _tiles = source._tiles;
        _surfaces = source._surfaces;
        SurfaceCount = source.SurfaceCount;
    }

    [EngineRoutine(0x00412750)]
    internal override Vid CreateMirror() => new VidHardware(this);

    [EngineRoutine(0x00419750)]
    internal VidHardware(KnownObject id, Size size, AlienShooterWeaponRecord[] weapons, MyError? error)
        : base(id, new GameObject(SpriteClass.Primitive, GroundObjectSize), weapons, Map.DefaultSpriteSpeed)
    {
        FileType = GroundType;
        FrameSize = size;
        _format = HighColorFormat.Rgb565;

        int i = 0;
        int texture = 0;
        for (int top = 0, remainingHeight = size.Height; top < size.Height; top += TileSize, remainingHeight -= TileSize)
        {
            for (int left = 0, remainingWidth = size.Width; left < size.Width; left += TileSize, remainingWidth -= TileSize)
            {
                if (i != 0)
                {
                    CollectionsMarshal.AsSpan(_tiles)[i - 1].Next = i;
                }

                _tiles.Add(new HardwareTile(texture, new Size(Math.Min(TileSize, remainingWidth), Math.Min(TileSize, remainingHeight)), new Point(left, top)));
                texture += 2;
                i++;
            }
        }

        SurfaceCount = (short)texture;
        _surfaces = new TextureData[texture];
        for (int surface = 0; surface < texture; surface += 2)
        {
            var tileSize = _tiles[surface / 2].Size;
            var colorSize = tileSize;
            ushort[] colors = Texture.Allocate(ref colorSize, VidPixelFormat.R5G6B5, TextureFlags.None, error, out _, out var colorFormat);
            _surfaces[surface] = new TextureData(colors, colorSize, colorFormat, []);
            var depthSize = tileSize;
            ushort[] depths = Texture.Allocate(ref depthSize, VidPixelFormat.D16, TextureFlags.SystemMemory, error, out _, out var depthFormat);
            depths.AsSpan().Fill(EmptyDepth);
            _surfaces[surface + 1] = new TextureData(depths, depthSize, depthFormat, []);
        }
    }

    internal short SurfaceCount { get; private set; }

    private static ReadOnlySpan<byte> TextureText => "texture"u8;

    /// <summary>
    /// Draws a built terrain sprite into every tile it touches.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    [EngineRoutine(0x00419b80)]
    public override void AddVidToTextures(Map map, Sprite sprite)
    {
        var vid = sprite.Vid;
        if (!FileType.HasFlag(VidType.Runs8Bit) || !FileType.HasFlag(VidType.ZBuffer) || Directions != 1 || vid.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
            return;

        var center = new Point((int)sprite.Position.X, (int)(sprite.Position.Y - sprite.Position.Z));
        var half = vid.FrameSize / 2;
        int i = 0;
        while (i < _tiles.Count)
        {
            var tile = _tiles[i];
            var tileHalf = tile.Size / 2;
            if (Math.Abs(tile.Origin.X - center.X + tile.Offset.X + tileHalf.Width) < half.Width + tileHalf.Width && Math.Abs(tile.Origin.Y - center.Y + tile.Offset.Y + tileHalf.Height) < half.Height + tileHalf.Height)
            {
                vid.DrawToVid(map, sprite, tile, _surfaces[tile.Texture], _surfaces[tile.Texture + 1], new Rectangle(tile.Offset, tile.Size));
            }

            if (tile.Next == 0)
                return;

            i = tile.Next;
        }
    }

    /// <summary>
    /// Draws the tiles of a sprite frame and, if the ground has depths, copies them into the depth buffer.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x0041a7f0)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (_tiles.Count == 0)
            return;

        var first = _tiles[sprite.Frame];
        if (first.Size.Height == 0 || IsHidden)
            return;

        var position = sprite.Position;
        int x = (int)(position.X - camera.X);
        int y = (int)(position.Y - position.Z - camera.Y);
        int z = (int)position.Z;
        float scaleX = Scale.X;
        float scaleY = Scale.Y;
        ref readonly var weapon = ref Weapon;
        if (HasScaleKeyframes)
        {
            scaleX *= weapon.Interpolate(weapon.ScaleXKeys, (int)sprite.KeyframePosition, sprite.KeyframePosition);
            scaleY *= weapon.Interpolate(weapon.ScaleYKeys, (int)sprite.KeyframePosition, sprite.KeyframePosition);
        }

        if (HasOffsetKeyframes)
        {
            x += (int)weapon.Interpolate(weapon.OffsetXKeys, (int)sprite.KeyframePosition, sprite.KeyframePosition);
            y += (int)weapon.Interpolate(weapon.OffsetYKeys, (int)sprite.KeyframePosition, sprite.KeyframePosition);
            z += (int)weapon.Interpolate(weapon.OffsetZKeys, (int)sprite.KeyframePosition, sprite.KeyframePosition);
        }

        int iterations = 1;
        var properties = Properties;
        if (properties.HasFlag(ObjectProperties.Blur) && first.Size.Width != 0)
        {
            int columns = Math.Abs((int)(sprite.PreviousPosition.X - position.X)) / first.Size.Width;
            int rows = Math.Abs((int)(sprite.PreviousPosition.Y - sprite.PreviousPosition.Z - position.Y + position.Z)) / first.Size.Height;
            iterations = Math.Max(columns, rows) * 2 + 1;
        }

        bool hasDepth = FileType.HasFlag(VidType.ZBuffer);
        var gamma = GetGamma(map, sprite);
        var graphics = map.Session.Graphics;
        SetBlend(graphics);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (properties.HasFlag(ObjectProperties.Blur))
            {
                x = (int)(position.X - camera.X + (sprite.PreviousPosition.X - position.X) * iteration / iterations);
                y = (int)(position.Y - position.Z - camera.Y + (sprite.PreviousPosition.Y - sprite.PreviousPosition.Z - position.Y + position.Z) * iteration / iterations);
                z = (int)((sprite.PreviousPosition.Z - position.Z) * iteration / iterations + position.Z);
            }

            if (!properties.HasFlag(ObjectProperties.AlwaysTop) && !hasDepth)
            {
                if (x < screen.Viewport.Left || x >= screen.Viewport.Right || y < screen.Viewport.Top || y >= screen.Viewport.Bottom || screen.Depth[screen.DepthWidth * y + x] > z * 8 + DepthBias)
                    continue;
            }

            DrawTiles(screen, map, first, new Point(x, y), z, new Vector2(scaleX, scaleY), gamma, hasDepth);
        }
    }

    /// <inheritdoc/>
    [EngineRoutine(0x0041a730)]
    public override void SetLayer()
    {
        var properties = Properties;
        if (properties.HasFlag(ObjectProperties.Ground))
        {
            Layer = ObjectLayer.Ground2;
        }
        else if (SpriteType == OldSpriteType.Region)
        {
            Layer = ObjectLayer.Region;
        }
        else if (SurfaceCount == 0)
        {
            Layer = ObjectLayer.Menu;
        }
        else if (FileType.HasFlag(VidType.ZBuffer))
        {
            Layer = FileType.HasFlag(VidType.Blended) ? ObjectLayer.AlphaGround : ObjectLayer.Ground;
        }
        else if (RecordId == KnownObject.Cursor)
        {
            Layer = ObjectLayer.Cursor;
        }
        else if (FileType.HasFlag(VidType.Blended))
        {
            Layer = properties.HasFlag(ObjectProperties.Wave) ? ObjectLayer.AlphaGround : ObjectLayer.Alpha;
        }
        else
        {
            Layer = ObjectLayer.Hardware;
        }
    }

    [EngineRoutine(0x00419d20)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, MyError? error)
    {
        FrameSize = file.Size;
        SurfaceCount = file.SurfaceCount;
        if (!file.HasSurfaceChunk)
        {
            Report(error, LogError.Load, 0, "SURF"u8);
        }

        if (SurfaceCount == 0)
            return;

        _surfaces = new TextureData[SurfaceCount];
        var surfaces = file.DecodeSurfaces();
        byte[] buffer = new byte[StreamBufferSize];
        Span<byte> paletteBytes = stackalloc byte[PaletteBytes];
        bool hasDepth = FileType.HasFlag(VidType.ZBuffer);
        for (int i = 0; i < SurfaceCount; i++)
        {
            if (i >= surfaces.Length)
            {
                Report(error, LogError.Create, 0, TextureText);
                return;
            }

            var surface = surfaces[i];
            if (!TryLoadSurface(surface, i, ref buffer, paletteBytes, error, out var texture))
                return;

            _surfaces[i] = texture;

            if (hasDepth)
            {
                i++;
                if (i >= surfaces.Length)
                    continue;

                LoadDepthSurface(surfaces[i], surface.Size, i, error);
            }
        }

        if (file.FrameRecords.Length == 0)
        {
            Report(error, LogError.Load, 0, "DATA"u8);
        }

        if (file.Type.HasFlag(VidType.CurrentVersion) && file.TextureFrameCount == 0)
        {
            Report(error, LogError.Load, 0, "tex_coor"u8);
        }

        LoadTiles(file);
    }

    private void SetBlend(Graphics graphics)
    {
        if (!FileType.HasFlag(VidType.Blended))
        {
            graphics.SetRenderState(RenderState.AlphaBlendEnable, 0);
        }
        else if (FileType.HasFlag(VidType.Runs8Bit))
        {
            graphics.SetAlphaBlend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
        else
        {
            graphics.SetAlphaBlend(BlendFactor.DestinationColor, BlendFactor.One);
        }
    }

    private void DrawTiles(Screen screen, Map map, HardwareTile first, Point origin, int z, Vector2 scale, Gamma gamma, bool hasDepth)
    {
        for (var tile = first; ; tile = _tiles[tile.Next])
        {
            int left = (int)((tile.Origin.X - FrameSize.Center.X) * scale.X) + origin.X;
            int top = (int)((tile.Origin.Y - FrameSize.Center.Y) * scale.Y) + origin.Y;
            if (left + tile.Size.Width >= screen.Viewport.Left && left < screen.Viewport.Right && top + tile.Size.Height >= screen.Viewport.Top && top < screen.Viewport.Bottom)
            {
                DrawTile(screen, map, tile, new Point(left, top), z, scale, gamma, hasDepth);
            }

            if (tile.Next == 0)
                break;
        }
    }

    private void DrawTile(Screen screen, Map map, in HardwareTile tile, Point corner, int z, Vector2 scale, Gamma gamma, bool hasDepth)
    {
        int width = tile.Size.Width;
        int height = tile.Size.Height;
        if (hasDepth)
        {
            width = Math.Min(width, screen.Viewport.Right - corner.X);
            height = Math.Min(height, screen.Viewport.Bottom - corner.Y);
        }

        var destination = Rectangle.FromLTRB(corner.X, corner.Y, corner.X + (int)(width * scale.X), corner.Y + (int)(height * scale.Y));
        var source = new Rectangle(tile.Offset.X, tile.Offset.Y, width, height);
        if (hasDepth)
        {
            var depth = _surfaces[tile.Texture + 1];
            Graphics.CopyDepth(screen, destination, source, depth.Texels, depth.Size.Width);
        }

        var properties = Properties;
        float topDepth = z * DepthUnit + DepthBase;
        if (properties.HasFlag(ObjectProperties.Wave) && !TopZ.IsExactlyZero())
        {
            topDepth = new Angle((byte)(map.Time >> WaveTimeShift & WaveTableMask)).Sine * TopZ * DepthUnit * 0.5f + topDepth;
        }

        float bottomDepth;
        if (!properties.HasFlag(ObjectProperties.AlwaysTop) && !hasDepth)
        {
            if (Size.Z <= Size.Y)
            {
                bottomDepth = topDepth;
            }
            else
            {
                bottomDepth = topDepth - height * DepthUnit;
                topDepth = height * DepthUnit + topDepth;
            }
        }
        else
        {
            bottomDepth = FrontDepth;
            topDepth = FrontDepth;
        }

        _surfaces[tile.Texture].View.DrawQuad(screen, destination, source, gamma, map.Session.Graphics.Blend, false, topDepth, bottomDepth);
    }

    private bool TryLoadSurface(in VidTexture surface, int index, ref byte[] buffer, Span<byte> paletteBytes, MyError? error, out TextureData texture)
    {
        texture = default;
        bool hasPalette = FileType.HasFlag(VidType.Palette);
        VidPixelFormat format;
        if (FileType.HasFlag(VidType.SurfaceVariant))
        {
            format = FileType.HasFlag(BlendedRuns) ? VidPixelFormat.Dxt3 : VidPixelFormat.Dxt1;
        }
        else if (hasPalette)
        {
            format = VidPixelFormat.P8;
        }
        else
        {
            format = FileType.HasFlag(BlendedRuns) ? VidPixelFormat.A4R4G4B4 : VidPixelFormat.R5G6B5;
        }

        var size = surface.Size;
        ushort[] texels = Texture.Allocate(ref size, format, TextureFlags.None, error, out _, out format);
        if (texels.Length == 0)
        {
            Report(error, LogError.Create, 0, TextureText);
            return false;
        }

        uint[] palette = [];
        surface.Palette.Span.CopyTo(paletteBytes);
        var colors = MemoryMarshal.Cast<byte, VidPackedColor>(paletteBytes);
        if (hasPalette)
        {
            Report(error, LogError.Text, format == VidPixelFormat.P8 ? 1 : 0, "palette %i"u8);
            if (format == VidPixelFormat.P8)
            {
                palette = new uint[colors.Length];
                for (int entry = 0; entry < palette.Length; entry++)
                {
                    palette[entry] = Gamma.OpaqueAlpha | colors[entry].Rgb;
                }
            }
        }

        if (surface.Failure == VidSurfaceFailure.Decode)
        {
            Report(error, LogError.Load, index, "Can't decode"u8);
        }

        int area = surface.Size.Width * surface.Size.Height;
        var stream = surface.Data.Span;
        if (!hasPalette && surface.StreamSize < area * sizeof(ushort))
        {
            Report(error, LogError.Text, 0, "Load DXT"u8);
            stream.CopyTo(MemoryMarshal.AsBytes(texels.AsSpan()));
        }
        else
        {
            int length = Math.Max(stream.Length, area * sizeof(ushort));
            if (buffer.Length < length)
            {
                buffer = new byte[length];
            }

            stream.CopyTo(buffer);
            ConvertTexels(buffer, texels, surface.Size, size.Width, format, colors);
        }

        texture = new TextureData(texels, size, format, palette);
        return true;
    }

    private void ConvertTexels(ReadOnlySpan<byte> buffer, Span<ushort> texels, Size size, int pitch, VidPixelFormat format, ReadOnlySpan<VidPackedColor> colors)
    {
        bool hasPalette = FileType.HasFlag(VidType.Palette);
        int width = size.Width;
        var pixels = MemoryMarshal.Cast<byte, ushort>(buffer);
        for (int row = 0; row < size.Height; row++)
        {
            var target = texels.Slice(row * pitch, width);
            if (hasPalette)
            {
                var indexes = buffer.Slice(row * width, width);
                if (format == VidPixelFormat.P8)
                {
                    for (int x = 0; x < width; x++)
                    {
                        target[x] = indexes[x];
                    }
                }
                else
                {
                    for (int x = 0; x < width; x++)
                    {
                        var color = colors[indexes[x]];
                        target[x] = (ushort)(color.Green << _format.GreenShift & _format.GreenMask | (color.Red & HighChannelMask) << _format.RedShift | color.Blue >> LowBlueBits & BlueMask);
                    }
                }
            }
            else
            {
                var line = pixels.Slice(row * width, width);
                if (format is VidPixelFormat.R5G6B5 or VidPixelFormat.A4R4G4B4)
                {
                    line.CopyTo(target);
                }
                else
                {
                    for (int x = 0; x < width; x++)
                    {
                        target[x] = (ushort)(line[x] >> 1 & Rgb555Mask | line[x] & BlueMask);
                    }
                }
            }
        }
    }

    private void LoadDepthSurface(in VidTexture depthSurface, Size size, int index, MyError? error)
    {
        var depthSize = size;
        ushort[] depths = Texture.Allocate(ref depthSize, VidPixelFormat.D16, TextureFlags.SystemMemory, error, out _, out var depthFormat);
        if (depths.Length == 0)
        {
            Report(error, LogError.Create, 0, TextureText);
        }
        else if (depthSurface.StreamSize != size.Height * depthSize.Width * sizeof(ushort))
        {
            Report(error, LogError.Load, depthSurface.StreamSize, "ZBuffer: invalid size"u8);
        }
        else
        {
            if (depthSurface.Failure == VidSurfaceFailure.DepthDecode)
            {
                Report(error, LogError.Load, depthSurface.DecodedSize, "Can't decode z"u8);
            }

            depthSurface.Data.Span.CopyTo(MemoryMarshal.AsBytes(depths.AsSpan()));
        }

        _surfaces[index] = new TextureData(depths, depthSize, depthFormat, []);
    }

    private void LoadTiles(in VidResource file)
    {
        for (int i = 0; i < file.TextureFrameCount; i++)
        {
            file.TryGetTextureFrame(i, out var frame);
            if (frame.Surface < 0 || frame.Surface >= _surfaces.Length || _surfaces[frame.Surface].IsEmpty)
            {
                _tiles.Clear();
                return;
            }

            _tiles.Add(new HardwareTile(frame.Surface, frame.Source.Size, frame.Origin, frame.Source.Location) { Next = frame.Next });
        }
    }
}

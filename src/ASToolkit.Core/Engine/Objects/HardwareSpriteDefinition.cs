using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Engine.IO;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a definition drawn from textures as textured quads, either loaded from the surfaces of a sprite file or the prerendered ground that built terrain sprites are baked into.
/// </summary>
public sealed class HardwareSpriteDefinition : SpriteDefinition
{
    private const int TileSize = 256;
    private const ushort EmptyDepth = 0x400;
    private const int DepthBias = 0x400;

    /// <summary>
    /// Represents the flags of the hardware ground, which the engine builds instead of loading from a sprite file.
    /// </summary>
    public const VidType GroundType = VidType.Runs8Bit | VidType.ZBuffer | VidType.Hardware | VidType.FrameVector;
    private const VidType BlendedRuns = VidType.Blended | VidType.Runs8Bit;
    private static readonly Vector3 GroundObjectSize = new(256, 256, 1);

    private static readonly ConditionalWeakTable<byte[], Screen?[]> DecodedTextures = new();

    private readonly List<HardwareTile> _tiles = [];
    private int _surfaceCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareSpriteDefinition"/> class as the ground, which covers an area with tiles.
    /// </summary>
    /// <param name="id">The object ID of the ground.</param>
    /// <param name="size">The size of the area, in pixels.</param>
    [EngineRoutine(0x00419750)]
    public HardwareSpriteDefinition(KnownObject id, Size size)
        : base(id, new GameObject(SpriteClass.Primitive, GroundObjectSize), default, SpriteCatalog.DefaultSpriteSpeed)
    {
        FileType = GroundType;
        Size = size;

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

                var tileSize = new Size(Math.Min(TileSize, remainingWidth), Math.Min(TileSize, remainingHeight));
                var target = new Screen(tileSize, false, HighColorFormat.Rgb565);
                target.Depth.Fill(EmptyDepth);
                _tiles.Add(new HardwareTile(texture, tileSize, new Point(left, top), target));
                texture += 2;
                i++;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareSpriteDefinition"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    public HardwareSpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames)
        : base(id, gameObject, weapon, file, totalFrames)
    {
    }

    /// <summary>
    /// Gets the size of the sprite, in pixels.
    /// </summary>
    public Size Size { get; private set; }

    [EngineRoutine(0x00419d20)]
    internal override void Load(in VidResource file, ReadOnlyMemory<byte> data, GameLogger? logger)
    {
        Size = file.Size;
        _surfaceCount = file.SurfaceCount;
        var textures = MemoryMarshal.TryGetArray(file.Data, out var source) && source.Array is not null ? DecodedTextures.GetValue(source.Array, _ => []) : [];
        if (textures.Length == 0)
        {
            var surfaces = file.DecodeSurfaces();
            textures = new Screen?[surfaces.Length];
            bool hasDepth = FileType.HasFlag(VidType.ZBuffer);
            for (int i = 0; i < surfaces.Length; i++)
            {
                var surface = surfaces[i];
                var target = new Screen(surface.Size, false, HighColorFormat.Rgb565);
                if (!surface.Palette.IsEmpty)
                {
                    var palette = MemoryMarshal.Cast<byte, VidPackedColor>(surface.Palette.Span);
                    target.Palette = new uint[palette.Length];
                    for (int entry = 0; entry < palette.Length; entry++)
                    {
                        target.Palette[entry] = Gamma.OpaqueAlpha | palette[entry].Rgb;
                    }

                    var indexes = surface.Data.Span;
                    for (int pixel = 0; pixel < target.HighColorPixels.Length; pixel++)
                    {
                        target.HighColorPixels[pixel] = indexes[pixel];
                    }
                }
                else if (FileType.HasFlag(VidType.SurfaceVariant))
                {
                    var format = FileType.HasFlag(BlendedRuns) ? VidPixelFormat.Dxt3 : VidPixelFormat.Dxt1;
                    var texels = Texture.Create(surface.Data.Span, surface.Size, format, default, out _);
                    texels.AsSpan(0, Math.Min(texels.Length, target.HighColorPixels.Length)).CopyTo(target.HighColorPixels);
                }
                else
                {
                    surface.Pixels[..Math.Min(surface.Pixels.Length, target.HighColorPixels.Length)].CopyTo(target.HighColorPixels);
                }

                textures[i] = target;
                if (hasDepth && i + 1 < surfaces.Length)
                {
                    i++;
                    surfaces[i].Pixels.CopyTo(target.Depth);
                    textures[i] = target;
                }
            }

            if (source.Array is not null)
            {
                DecodedTextures.AddOrUpdate(source.Array, textures);
            }
        }

        for (int i = 0; i < file.TextureFrameCount; i++)
        {
            file.TryGetTextureFrame(i, out var frame);
            var texture = (uint)frame.Surface < (uint)textures.Length ? textures[frame.Surface] : null;
            if (texture is null)
            {
                _tiles.Clear();
                return;
            }

            _tiles.Add(new HardwareTile(frame.Surface, frame.Source.Size, frame.Origin, texture, frame.Source.Location) { Next = frame.Next });
        }
    }

    /// <summary>
    /// Draws a built terrain sprite into every tile it touches.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    [EngineRoutine(0x00419b80)]
    public override void AddVidToTextures(Map map, Sprite sprite)
    {
        var vid = sprite.Definition;
        if (!FileType.HasFlag(VidType.Runs8Bit) || !FileType.HasFlag(VidType.ZBuffer) || Layout.Directions != 1 || vid.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
            return;

        int x = (int)sprite.Position.X;
        int y = (int)(sprite.Position.Y - sprite.Position.Z);
        var half = vid.FrameSize / 2;
        int i = 0;
        while (i < _tiles.Count)
        {
            var tile = _tiles[i];
            var tileHalf = tile.Size / 2;
            if (Math.Abs(tile.Origin.X - x + tile.Offset.X + tileHalf.Width) < half.Width + tileHalf.Width && Math.Abs(tile.Origin.Y - y + tile.Offset.Y + tileHalf.Height) < half.Height + tileHalf.Height)
            {
                tile.Target.Clip = new Rectangle(tile.Offset, tile.Size);
                vid.DrawToVid(tile.Target, map, sprite, tile);
            }

            if (tile.Next == 0)
                return;

            i = tile.Next;
        }
    }

    /// <summary>
    /// Draws the tiles of a sprite frame, one textured quad each, and copies their depths into the depth buffer when the ground holds depths.
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
        float scaleX = Object.Scale.X;
        float scaleY = Object.Scale.Y;
        ref readonly var weapon = ref Weapon;
        if (HasScaleKeyframes)
        {
            scaleX *= weapon.Interpolate(weapon.ScaleXKeys, sprite.KeyframePosition);
            scaleY *= weapon.Interpolate(weapon.ScaleYKeys, sprite.KeyframePosition);
        }

        if (HasOffsetKeyframes)
        {
            x += (int)weapon.Interpolate(weapon.OffsetXKeys, sprite.KeyframePosition);
            y += (int)weapon.Interpolate(weapon.OffsetYKeys, sprite.KeyframePosition);
            z += (int)weapon.Interpolate(weapon.OffsetZKeys, sprite.KeyframePosition);
        }

        int iterations = 1;
        var properties = Object.Properties;
        if (properties.HasFlag(ObjectProperties.Blur) && first.Size.Width != 0)
        {
            int columns = Math.Abs((int)(sprite.PreviousPosition.X - position.X)) / first.Size.Width;
            int rows = Math.Abs((int)(sprite.PreviousPosition.Y - sprite.PreviousPosition.Z - position.Y + position.Z)) / first.Size.Height;
            iterations = Math.Max(columns, rows) * 2 + 1;
        }

        bool hasDepth = FileType.HasFlag(VidType.ZBuffer);
        var gamma = GetGamma(map, sprite);
        var blend = FileType.HasFlag(VidType.Blended) ? FileType.HasFlag(VidType.Runs8Bit) ? QuadBlend.Alpha : QuadBlend.ColorAdd : QuadBlend.None;
        var format = FileType.HasFlag(VidType.Palette) ? VidPixelFormat.P8 : FileType.HasFlag(BlendedRuns) ? VidPixelFormat.A4R4G4B4 : VidPixelFormat.R5G6B5;
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
                if (x < screen.Clip.Left || x >= screen.Clip.Right || y < screen.Clip.Top || y >= screen.Clip.Bottom || screen.Depth[screen.DepthWidth * y + x] > z * 8 + DepthBias)
                    continue;
            }

            for (var tile = first; ; tile = _tiles[tile.Next])
            {
                int left = (int)((tile.Origin.X - Size.Center.X) * scaleX) + x;
                int top = (int)((tile.Origin.Y - Size.Center.Y) * scaleY) + y;
                int width = tile.Size.Width;
                int height = tile.Size.Height;
                if (left + width >= screen.Clip.Left && left < screen.Clip.Right && top + height >= screen.Clip.Top && top < screen.Clip.Bottom)
                {
                    if (hasDepth)
                    {
                        width = Math.Min(width, screen.Clip.Right - left);
                        height = Math.Min(height, screen.Clip.Bottom - top);
                    }

                    var destination = Rectangle.FromLTRB(left, top, left + (int)(width * scaleX), top + (int)(height * scaleY));
                    var source = new Rectangle(tile.Offset.X, tile.Offset.Y, width, height);
                    if (hasDepth)
                    {
                        Graphics.CopyDepth(screen, destination, source, tile.Target.Depth, tile.Target.DepthWidth);
                    }

                    new Texture(tile.Target.HighColorPixels, tile.Target.Size, format, tile.Target.Palette).DrawQuad(screen, destination, source, gamma, blend);
                }

                if (tile.Next == 0)
                    break;
            }
        }
    }

    /// <inheritdoc/>
    [EngineRoutine(0x0041a730)]
    public override void SetLayer()
    {
        var properties = Object.Properties;
        if (properties.HasFlag(ObjectProperties.Ground))
        {
            Layer = ObjectLayer.Ground2;
        }
        else if (Object.SpriteType == SpriteType.Region)
        {
            Layer = ObjectLayer.Region;
        }
        else if (_surfaceCount == 0)
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
}

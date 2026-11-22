using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the definition class of built terrain, drawn in software from runs of pixels into a 16-bit back buffer and into the textures of the hardware ground.
/// </summary>
public sealed class HighColorSpriteDefinition : SoftwareSpriteDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HighColorSpriteDefinition"/> class.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    /// <param name="format">The channel layout of the 16-bit back buffer.</param>
    public HighColorSpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames, HighColorFormat format)
        : base(id, gameObject, weapon, file, totalFrames, format)
    {
    }

    /// <inheritdoc/>
    protected override bool IsHighColor => true;

    /// <summary>
    /// Gets the size of one palette in bytes, with 16-bit colors unless the sprite file is blended.
    /// </summary>
    /// <returns>The size of one palette.</returns>
    [EngineRoutine(0x00412710)]
    public override int GetPaletteSize() => FileType.HasFlag(VidType.Blended) ? TrueColorPaletteSize : HighColorPaletteSize;

    /// <summary>
    /// Draws a sprite with 16-bit pixels from its runs.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00417780)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (IsHidden)
            return;

        var origin = new Point((int)sprite.Position.X - (int)camera.X - FrameSize.Center.X, (int)(sprite.Position.Y - sprite.Position.Z) - (int)camera.Y - FrameSize.Center.Y);
        if (IsOutside(screen.Clip, origin, FrameSize))
            return;

        Span<byte> scratch = stackalloc byte[PaletteScratchSize];
        DrawRuns(screen, map, sprite, origin, SelectPalette(map, sprite, Palettes, scratch));
    }

    /// <summary>
    /// Draws a sprite with 16-bit pixels into the color and depth of a tile of the hardware ground.
    /// </summary>
    /// <param name="target">The color and depth of the tile.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="tile">The tile.</param>
    [EngineRoutine(0x00418940)]
    public override void DrawToVid(Screen target, Map map, Sprite sprite, HardwareTile tile)
    {
        if (IsHidden)
            return;

        float worldX = sprite.Position.X - FrameSize.Center.X;
        worldX -= tile.Origin.X;
        worldX -= tile.Offset.X;
        float worldY = sprite.Position.Y - sprite.Position.Z - FrameSize.Center.Y;
        worldY -= tile.Origin.Y;
        worldY -= tile.Offset.Y;
        var origin = new Point((int)worldX, (int)worldY);
        if (IsOutside(target.Clip, origin, FrameSize))
            return;

        DrawRuns(target, map, sprite, origin, Palettes[GetPaletteOffset(sprite)..]);
    }

    private void DrawRuns(Screen target, Map map, Sprite sprite, Point origin, ReadOnlySpan<byte> paletteBytes)
    {
        var clip = target.Clip;
        int depth = GetDepth(map, sprite, ref origin);
        if (!ReadFrame(Frames, clip, FrameOffsets[sprite.Frame], origin.Y, out int top, out int rowCount, out int end, out int position))
            return;

        bool clipped = origin.X < clip.Left || clip.Right < origin.X + FrameSize.Width;
        var depths = target.Depth;
        var colors = target.HighColorPixels;
        int depthPitch = target.DepthWidth;
        int pitch = target.Pitch;
        var palette = MemoryMarshal.Cast<byte, ushort>(paletteBytes);
        if (!(FileType.HasFlag(VidType.Blended) && FileType.HasFlag(VidType.Runs8Bit) && FileType.HasFlag(VidType.Palette)))
        {
            if (!FileType.HasFlag(VidType.Runs8Bit))
                return;

            if (!FileType.HasFlag(VidType.Palette) || !FileType.HasFlag(VidType.ZBuffer))
            {
                bool isPalette = FileType.HasFlag(VidType.Palette);
                int pixelSize = isPalette ? sizeof(byte) : sizeof(ushort);
                depth = BiasDepth(depth);
                int step = 0;
                SkipRows(Frames, ref position, ref top, ref rowCount, clip.Top, pixelSize);
                if (HasSlope())
                {
                    depth += rowCount * Cell;
                    step = SlopeStep;
                }

                int colorRow = top * pitch;
                int depthRow = top * depthPitch;
                for (int row = top; row < end; row++)
                {
                    int x = origin.X;
                    while (!RunAt(Frames, position).IsEnd)
                    {
                        ref readonly var run = ref RunAt(Frames, position);
                        int start = x + run.Skip;
                        int length = run.Count;
                        int pixels = position + RunHeader;
                        bool visible = ClipRunToRow(clip, clipped, start, length, out int first, out int offset, out int count);
                        var words = MemoryMarshal.Cast<byte, ushort>(Frames[pixels..]);
                        for (int i = 0; visible && i < count; i++)
                        {
                            if (depths[depthRow + first + i] <= depth)
                            {
                                depths[depthRow + first + i] = (ushort)depth;
                                colors[colorRow + first + i] = isPalette ? palette[Frames[pixels + offset + i]] : words[offset + i];
                            }
                        }

                        position = pixels + length * pixelSize;
                        x = start + length;
                    }

                    position += RunHeader;
                    colorRow += pitch;
                    depthRow += depthPitch;
                    depth += step;
                }
            }
            else
            {
                DrawDepthRuns(target, colors, palette, Frames, origin, clipped, position, top, rowCount, end, (short)depth);
            }
        }
        else
        {
            DrawBlendedRuns(target, MemoryMarshal.Cast<byte, uint>(paletteBytes), Frames, origin, clipped, position, top, rowCount, end, depth);
        }
    }

    /// <summary>
    /// Applies a gamma to a palette of 16-bit colors, or of 32-bit colors when the sprite file is blended.
    /// </summary>
    /// <param name="palette">The palette, 256 entries of the size the sprite file stores them in.</param>
    /// <param name="gamma">The gamma.</param>
    [EngineRoutine(0x00416030)]
    public override void ApplyGamma(Span<byte> palette, Gamma gamma)
    {
        if (palette.IsEmpty || gamma is { Negative: 0, Positive: 0 })
            return;

        if (FileType.HasFlag(VidType.Blended))
        {
            base.ApplyGamma(palette, gamma);
            return;
        }

        var pixels = MemoryMarshal.Cast<byte, ushort>(palette);
        for (int i = 0; i < PaletteEntries; i++)
        {
            uint color = Format.Expand(pixels[i]);
            uint blue = (uint)((pixels[i] & HighColorBlueMask) << 3);
            uint green = color >> Gamma.ChannelBits & byte.MaxValue;
            uint red = color >> (2 * Gamma.ChannelBits) & byte.MaxValue;
            pixels[i] = Format.Pack(gamma.Apply(red << (2 * Gamma.ChannelBits) | green << Gamma.ChannelBits | blue));
        }
    }
}

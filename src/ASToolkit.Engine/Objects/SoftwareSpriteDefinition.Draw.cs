using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

public partial class SoftwareSpriteDefinition
{
    private protected const float HeightScale = 8f;
    private protected const int LiftedDepth = 0x3fff;
    private protected const int LiftedLimit = 0x3ffe;
    private protected const int DepthBias = 0x400;
    private protected const int DepthLimit = 0x7fff;
    private protected const int SlopeStep = -8;
    private protected const int WaveTableMask = 0xff;
    private protected const int WaveTimeShift = 3;
    private protected const int RunHeader = 2;
    private protected const int PaletteScratchSize = 1024;
    private protected const int AlphaShift = 24;
    private protected const int Cell = 8;

    /// <summary>
    /// Draws a sprite with 32-bit pixels from its runs.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00416860)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
        if (IsHidden)
            return;

        var origin = new Point((int)sprite.Position.X - (int)camera.X - FrameSize.Center.X, (int)(sprite.Position.Y - sprite.Position.Z) - (int)camera.Y - FrameSize.Center.Y);
        var clip = screen.Clip;
        if (IsOutside(clip, origin, FrameSize))
            return;

        int depth = GetDepth(map, sprite, ref origin);
        if (!ReadFrame(Frames, clip, FrameOffsets[sprite.Frame], origin.Y, out int top, out int rowCount, out int end, out int position))
            return;

        bool clipped = origin.X < clip.Left || clip.Right < origin.X + FrameSize.Width;
        Span<byte> scratch = stackalloc byte[PaletteScratchSize];
        var palette = MemoryMarshal.Cast<byte, uint>(SelectPalette(map, sprite, Palettes, scratch));
        if (!(FileType.HasFlag(VidType.Blended) && FileType.HasFlag(VidType.Runs8Bit) && FileType.HasFlag(VidType.Palette)))
        {
            if (!FileType.HasFlag(VidType.Runs8Bit))
                return;

            if (!FileType.HasFlag(VidType.Palette) || !FileType.HasFlag(VidType.ZBuffer))
            {
                if (!FileType.HasFlag(VidType.Palette))
                {
                    depth = BiasDepth(depth);
                    int step = 0;
                    SkipRows(Frames, ref position, ref top, ref rowCount, clip.Top, sizeof(ushort));
                    if (HasSlope())
                    {
                        depth += rowCount * Cell;
                        step = SlopeStep;
                    }

                    int colorRow = top * screen.Pitch;
                    int depthRow = top * screen.DepthWidth;
                    for (int row = top; row < end; row++)
                    {
                        int x = origin.X;
                        while (!RunAt(Frames, position).IsEnd)
                        {
                            ref readonly var run = ref RunAt(Frames, position);
                            int start = x + run.Skip;
                            int pixels = position + RunHeader;
                            bool visible = ClipRunToRow(clip, clipped, start, run.Count, out int first, out int offset, out int count);
                            var words = MemoryMarshal.Cast<byte, ushort>(Frames[pixels..]);
                            for (int i = 0; visible && i < count; i++)
                            {
                                if (screen.Depth[depthRow + first + i] <= depth)
                                {
                                    screen.Depth[depthRow + first + i] = (ushort)depth;
                                    screen.TrueColorPixels[colorRow + first + i] = screen.Format.Expand(words[offset + i]);
                                }
                            }

                            position = pixels + run.Count * sizeof(ushort);
                            x = start + run.Count;
                        }

                        position += RunHeader;
                        colorRow += screen.Pitch;
                        depthRow += screen.DepthWidth;
                        depth += step;
                    }
                }
                else
                {
                    depth = BiasDepth(depth);
                    int step = 0;
                    SkipRows(Frames, ref position, ref top, ref rowCount, clip.Top, sizeof(byte));
                    if (HasSlope())
                    {
                        depth += rowCount * Cell;
                        step = SlopeStep;
                    }

                    short key = (short)depth;
                    int colorRow = top * screen.Pitch;
                    int depthRow = top * screen.DepthWidth;
                    for (int row = top; row < end; row++)
                    {
                        int x = origin.X;
                        while (!RunAt(Frames, position).IsEnd)
                        {
                            ref readonly var run = ref RunAt(Frames, position);
                            int start = x + run.Skip;
                            int pixels = position + RunHeader;
                            bool visible = ClipRunToRow(clip, clipped, start, run.Count, out int first, out int offset, out int count);
                            if (visible && clipped)
                            {
                                DrawPaletteRun(screen, palette, Frames[(pixels + offset)..], depthRow + first, colorRow + first, count, key);
                            }
                            else if (visible)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    if (screen.Depth[depthRow + first + i] <= depth)
                                    {
                                        screen.Depth[depthRow + first + i] = (ushort)depth;
                                        screen.TrueColorPixels[colorRow + first + i] = palette[Frames[pixels + offset + i]];
                                    }
                                }
                            }

                            position = pixels + run.Count;
                            x = start + run.Count;
                        }

                        position += RunHeader;
                        colorRow += screen.Pitch;
                        depthRow += screen.DepthWidth;
                        depth += step;
                        key = (short)(key + step);
                    }
                }
            }
            else
            {
                DrawDepthRuns(screen, screen.TrueColorPixels, palette, Frames, origin, clipped, position, top, rowCount, end, (short)depth);
            }
        }
        else
        {
            DrawBlendedRuns(screen, palette, Frames, origin, clipped, position, top, rowCount, end, depth);
        }
    }

    /// <summary>
    /// Blends a color over another with the alpha of the source.
    /// </summary>
    /// <param name="destination">The color underneath, with blue in the lowest byte.</param>
    /// <param name="source">The color on top.</param>
    /// <param name="alpha">The alpha of the source, from 0 to 255.</param>
    /// <returns>The blended color with an opaque top byte.</returns>
    [EngineRoutine(0x00416680)]
    public static uint BlendColor(uint destination, uint source, int alpha)
    {
        int weight = alpha + 1;
        int inverse = 0x100 - weight;
        uint blue = BlendChannel((int)(destination & 0xff), (int)(source & 0xff), inverse, weight);
        uint green = BlendChannel((int)(destination >> Gamma.ChannelBits & 0xff), (int)(source >> Gamma.ChannelBits & 0xff), inverse, weight);
        uint red = BlendChannel((int)(destination >> (2 * Gamma.ChannelBits) & 0xff), (int)(source >> (2 * Gamma.ChannelBits) & 0xff), inverse, weight);

        return Gamma.OpaqueAlpha | red << (2 * Gamma.ChannelBits) | green << Gamma.ChannelBits | blue;
    }

    /// <summary>
    /// Blends a run of palette entries over 16-bit pixels where the depth buffer does not exceed the key, without changing the depth buffer.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="palette">The palette, with 32-bit entries.</param>
    /// <param name="indexes">The palette indexes of the run.</param>
    /// <param name="depthIndex">The index of the run's first depth value.</param>
    /// <param name="colorIndex">The index of the run's first pixel.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x00416740)]
    public static void BlendRun16(Screen screen, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> indexes, int depthIndex, int colorIndex, int count, ushort key)
    {
        for (int i = 0; i < count; i++)
        {
            var depths = screen.Depth;
            if (depths[depthIndex + i] <= key)
            {
                uint entry = palette[indexes[i]];
                var colors = screen.HighColorPixels;
                uint color = BlendColor(screen.Format.Expand(colors[colorIndex + i]), entry, (int)(entry >> AlphaShift));
                colors[colorIndex + i] = screen.Format.Pack(color);
            }
        }
    }

    /// <summary>
    /// Blends a run of palette entries over 32-bit pixels where the depth buffer does not exceed the key, without changing the depth buffer.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="palette">The palette, with 32-bit entries.</param>
    /// <param name="indexes">The palette indexes of the run.</param>
    /// <param name="depthIndex">The index of the run's first depth value.</param>
    /// <param name="colorIndex">The index of the run's first pixel.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x00416600)]
    public static void BlendRun32(Screen screen, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> indexes, int depthIndex, int colorIndex, int count, ushort key)
    {
        for (int i = 0; i < count; i++)
        {
            var depths = screen.Depth;
            if (depths[depthIndex + i] <= key)
            {
                uint entry = palette[indexes[i]];
                var colors = screen.TrueColorPixels;
                colors[colorIndex + i] = BlendColor(colors[colorIndex + i], entry, (int)(entry >> AlphaShift));
            }
        }
    }

    /// <summary>
    /// Draws a run of palette entries to 32-bit pixels where the depth buffer is below the key.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="palette">The palette, with 32-bit entries.</param>
    /// <param name="indexes">The palette indexes of the run.</param>
    /// <param name="depthIndex">The index of the run's first depth value.</param>
    /// <param name="colorIndex">The index of the run's first pixel.</param>
    /// <param name="count">The number of pixels; a run of none still draws one.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x004165c0)]
    public static void DrawPaletteRun(Screen screen, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> indexes, int depthIndex, int colorIndex, int count, short key)
    {
        int i = 0;
        bool more;
        do
        {
            var depths = screen.Depth;
            if ((short)depths[depthIndex + i] < key)
            {
                depths[depthIndex + i] = (ushort)key;
                var colors = screen.TrueColorPixels;
                colors[colorIndex + i] = palette[indexes[i]];
            }

            i++;
            more = 0 < count;
            count--;
        }
        while (count != 0 && more);
    }

    private protected static void DrawDepthRuns<TColor>(Screen target, Span<TColor> colors, ReadOnlySpan<TColor> palette, ReadOnlySpan<byte> data, Point origin, bool clipped, int position, int top, int rowCount, int end, short key)
    {
        var clip = target.Clip;
        SkipRows(data, ref position, ref top, ref rowCount, clip.Top, 3);
        int pitch = target.Pitch;
        int colorRow = top * pitch;
        int depthPitch = target.DepthWidth;
        int depthRow = top * depthPitch;
        for (int row = top; row < end; row++)
        {
            int x = origin.X;
            while (!RunAt(data, position).IsEnd)
            {
                ref readonly var run = ref RunAt(data, position);
                int start = x + run.Skip;
                int pixels = position + RunHeader;
                bool visible = ClipRunToRow(clip, clipped, start, run.Count, out int first, out int offset, out int count);
                if (visible)
                {
                    var depthValues = MemoryMarshal.Cast<byte, short>(data[pixels..]);
                    var zIndexes = data[(pixels + run.Count * sizeof(short))..];
                    int remaining = count;
                    int i = 0;
                    bool more;
                    do
                    {
                        short value = (short)(key + depthValues[offset + i]);
                        var depths = target.Depth;
                        if ((short)depths[depthRow + first + i] < value)
                        {
                            depths[depthRow + first + i] = (ushort)value;
                            colors[colorRow + first + i] = palette[zIndexes[offset + i]];
                        }

                        i++;
                        more = 0 < remaining;
                        remaining--;
                    }
                    while (remaining != 0 && more);
                }

                position = pixels + run.Count * 3;
                x = start + run.Count;
            }

            position += RunHeader;
            colorRow += pitch;
            depthRow += depthPitch;
        }
    }

    private protected void DrawBlendedRuns(Screen target, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> data, Point origin, bool clipped, int position, int top, int rowCount, int end, int depth)
    {
        depth = BiasDepth(depth);
        int step = 0;
        var clip = target.Clip;
        SkipRows(data, ref position, ref top, ref rowCount, clip.Top, sizeof(byte));
        short key = (short)depth;
        if (HasSlope())
        {
            key = (short)(key + (short)rowCount * Cell);
            step = SlopeStep;
        }

        int pitch = target.Pitch;
        int colorRow = top * pitch;
        int depthPitch = target.DepthWidth;
        int depthRow = top * depthPitch;
        for (int row = top; row < end; row++)
        {
            int x = origin.X;
            while (!RunAt(data, position).IsEnd)
            {
                ref readonly var run = ref RunAt(data, position);
                int start = x + run.Skip;
                int pixels = position + RunHeader;
                if (ClipRunToRow(clip, clipped, start, run.Count, out int first, out int offset, out int count))
                {
                    if (IsHighColor)
                    {
                        BlendRun16(target, palette, data[(pixels + offset)..], depthRow + first, colorRow + first, count, (ushort)key);
                    }
                    else
                    {
                        BlendRun32(target, palette, data[(pixels + offset)..], depthRow + first, colorRow + first, count, (ushort)key);
                    }
                }

                position = pixels + run.Count;
                x = start + run.Count;
            }

            position += RunHeader;
            colorRow += pitch;
            depthRow += depthPitch;
            key = (short)(key + step);
        }
    }

    private protected static bool IsOutside(Rectangle clip, Point origin, Size size) =>
        clip.Left > origin.X + size.Width || origin.X >= clip.Right || clip.Top > origin.Y + size.Height || origin.Y >= clip.Bottom;

    private protected int GetDepth(Map map, Sprite sprite, ref Point origin)
    {
        int depth = (int)(sprite.Position.Z * HeightScale);
        var properties = Object.Properties;
        if (properties.HasFlag(ObjectProperties.AlwaysTop) && depth <= LiftedLimit)
            return depth + LiftedDepth;

        if (properties.HasFlag(ObjectProperties.Wave))
        {
            int bob = (int)(new Angle((byte)(map.Time >> WaveTimeShift & WaveTableMask)).Sine * Object.StepHeight * HeightScale);
            depth += bob;
            origin.Y -= bob + (bob >> 31 & 7) >> 3;
        }

        return depth;
    }

    private protected static bool ReadFrame(ReadOnlySpan<byte> data, Rectangle clip, int record, int originY, out int top, out int rowCount, out int end, out int position)
    {
        int rows = record + MemoryMarshal.AsRef<VidFrameHeader>(data[record..]).Size;
        ref readonly var header = ref MemoryMarshal.AsRef<VidRowsHeader>(data[rows..]);
        top = header.First + originY;
        rowCount = header.Count;
        end = rowCount + top;
        position = rows + Unsafe.SizeOf<VidRowsHeader>();
        if (top >= clip.Bottom || clip.Top > end)
            return false;

        end = Math.Min(end, clip.Bottom);
        return true;
    }

    private protected ReadOnlySpan<byte> SelectPalette(Map map, Sprite sprite, ReadOnlySpan<byte> data, Span<byte> scratch)
    {
        if (!sprite.HasExtra || sprite.GammaOverride is { Negative: 0, Positive: 0 })
            return data[GetPaletteOffset(sprite)..];

        int paletteSize = GetPaletteSize();
        int offset = FileType.HasFlag(VidType.PaletteVariants) ? paletteSize * 4 : 0;
        var copy = scratch[..paletteSize];
        data.Slice(offset, paletteSize).CopyTo(copy);
        var gamma = sprite.GetGamma();
        ApplyGamma(copy, Object.Properties.HasFlag(ObjectProperties.Gamma) ? gamma : gamma.Add(map.Session.Graphics.Gamma));
        return scratch;
    }

    private protected int GetPaletteOffset(Sprite sprite) => FileType.HasFlag(VidType.PaletteVariants) ? GetPaletteSize() * ((int)sprite.Army & ArmyMask) : 0;

    private protected static int BiasDepth(int depth) => Math.Min(depth + DepthBias, DepthLimit);

    private protected bool HasSlope() => Object.Size.Y < Object.Size.Z;

    private protected static void SkipRows(ReadOnlySpan<byte> data, ref int position, ref int top, ref int rowCount, int clipTop, int pixelSize)
    {
        if (top >= clipTop)
            return;

        int skipped = clipTop - top;
        top += skipped;
        rowCount -= skipped;
        do
        {
            while (!RunAt(data, position).IsEnd)
            {
                position += RunAt(data, position).Count * pixelSize + RunHeader;
            }

            position += RunHeader;
            skipped--;
        }
        while (skipped != 0);
    }

    private protected static bool ClipRunToRow(Rectangle clip, bool clipped, int start, int length, out int first, out int offset, out int count)
    {
        first = start;
        offset = 0;
        count = length;
        if (!clipped)
            return true;

        int end = start + length;
        if (start < clip.Left)
        {
            first = clip.Left;
            offset = clip.Left - start;
            if (clip.Right < end)
            {
                count = clip.Right - clip.Left;
                return true;
            }

            count = end - clip.Left;
            return clip.Left < end;
        }

        if (clip.Right < end)
        {
            count = clip.Right - start;
            return start < clip.Right;
        }

        return true;
    }

    private protected static uint BlendChannel(int destination, int source, int inverse, int weight) => (uint)Math.Clamp(destination * inverse + source * weight >> Gamma.ChannelBits, 0, 0xff);

    private protected static ref readonly VidRunHeader RunAt(ReadOnlySpan<byte> data, int position) => ref MemoryMarshal.AsRef<VidRunHeader>(data[position..]);
}

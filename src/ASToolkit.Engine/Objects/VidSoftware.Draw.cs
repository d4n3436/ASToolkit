using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class VidSoftware
{
    private protected const float HeightScale = 8f;
    private protected const int LiftedDepth = 0x3fff;
    private protected const int LiftedLimit = 0x3ffe;
    private protected const int DepthBias = 0x400;
    private protected const int DepthLimit = 0x7fff;
    private protected const int SlopeStep = -8;
    private protected const int WaveTableMask = 0xff;
    private protected const int WaveTimeShift = 3;
    private protected const int PaletteScratchSize = 1024;
    private protected const int AlphaShift = 24;
    private protected const int Cell = 8;
    private const uint OutlineShadeColor = 0xa4a4a4;
    private const uint OutlineLightColor = 0x8f8f8f;

    private static readonly Size OutlineMargin = new(200, 100);
    private static readonly Vector2 OutlineSlant = new(0.35f, 0.7f);

    private bool HasSlope => Size.Y < Size.Z;

    /// <summary>
    /// Draws a run of palette entries to 32-bit pixels where the depth buffer is below the key.
    /// </summary>
    /// <param name="colors">The 32-bit pixels to draw to.</param>
    /// <param name="depths">The depth buffer of <paramref name="colors"/>.</param>
    /// <param name="palette">The palette, with 32-bit entries.</param>
    /// <param name="indexes">The palette indexes of the run.</param>
    /// <param name="depthIndex">The index of the run's first depth value.</param>
    /// <param name="colorIndex">The index of the run's first pixel.</param>
    /// <param name="count">The number of pixels; a run of zero pixels still draws one.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x004165c0)]
    public static void DrawPaletteRun(Span<uint> colors, Span<ushort> depths, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> indexes, int depthIndex, int colorIndex, int count, short key)
    {
        int i = 0;
        bool more;
        do
        {
            if ((short)depths[depthIndex + i] < key)
            {
                depths[depthIndex + i] = (ushort)key;
                colors[colorIndex + i] = palette[indexes[i]];
            }

            i++;
            more = 0 < count;
            count--;
        }
        while (count != 0 && more);
    }

    /// <summary>
    /// Blends a run of palette entries over 32-bit pixels where the depth buffer does not exceed the key, without changing the depth buffer.
    /// </summary>
    /// <param name="colors">The 32-bit pixels to draw to.</param>
    /// <param name="depths">The depth buffer of <paramref name="colors"/>.</param>
    /// <param name="palette">The palette, with 32-bit entries.</param>
    /// <param name="indexes">The palette indexes of the run.</param>
    /// <param name="depthIndex">The index of the run's first depth value.</param>
    /// <param name="colorIndex">The index of the run's first pixel.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x00416600)]
    public static void BlendRun32(Span<uint> colors, ReadOnlySpan<ushort> depths, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> indexes, int depthIndex, int colorIndex, int count, ushort key)
    {
        for (int i = 0; i < count; i++)
        {
            if (depths[depthIndex + i] <= key)
            {
                uint entry = palette[indexes[i]];
                colors[colorIndex + i] = BlendColor(colors[colorIndex + i], entry, (int)(entry >> AlphaShift));
            }
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
        uint blue = (uint)Math.Clamp((int)(destination & 0xff) * inverse + (int)(source & 0xff) * weight >> Gamma.ChannelBits, 0, 0xff);
        uint green = (uint)Math.Clamp((int)(destination >> Gamma.ChannelBits & 0xff) * inverse + (int)(source >> Gamma.ChannelBits & 0xff) * weight >> Gamma.ChannelBits, 0, 0xff);
        uint red = (uint)Math.Clamp((int)(destination >> (2 * Gamma.ChannelBits) & 0xff) * inverse + (int)(source >> (2 * Gamma.ChannelBits) & 0xff) * weight >> Gamma.ChannelBits, 0, 0xff);

        return Gamma.OpaqueAlpha | red << (2 * Gamma.ChannelBits) | green << Gamma.ChannelBits | blue;
    }

    /// <summary>
    /// Blends a run of palette entries over 16-bit pixels where the depth buffer does not exceed the key, without changing the depth buffer.
    /// </summary>
    /// <param name="colors">The 16-bit pixels to draw to.</param>
    /// <param name="depths">The depth buffer of <paramref name="colors"/>.</param>
    /// <param name="format">The layout of <paramref name="colors"/>.</param>
    /// <param name="palette">The palette, with 32-bit entries.</param>
    /// <param name="indexes">The palette indexes of the run.</param>
    /// <param name="depthIndex">The index of the run's first depth value.</param>
    /// <param name="colorIndex">The index of the run's first pixel.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="key">The depth of the sprite.</param>
    [EngineRoutine(0x00416740)]
    public static void BlendRun16(Span<ushort> colors, ReadOnlySpan<ushort> depths, HighColorFormat format, ReadOnlySpan<uint> palette, ReadOnlySpan<byte> indexes, int depthIndex, int colorIndex, int count, ushort key)
    {
        for (int i = 0; i < count; i++)
        {
            if (depths[depthIndex + i] <= key)
            {
                uint entry = palette[indexes[i]];
                uint color = BlendColor(format.Expand(colors[colorIndex + i]), entry, (int)(entry >> AlphaShift));
                colors[colorIndex + i] = format.Pack(color);
            }
        }
    }

    /// <summary>
    /// Draws a sprite with 32-bit pixels from its runs.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="shift">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00416860)]
    public override void Draw(Screen screen, Map map, Sprite sprite, Vector2 shift)
    {
        if (IsHidden)
            return;

        var origin = GetScreenOrigin(sprite, shift);
        if (!TryLocateFrame(map, sprite, screen.Viewport, ref origin, out int depth, out var rows))
            return;

        map.Session.Graphics.LockDepth();
        Span<byte> scratch = stackalloc byte[PaletteScratchSize];
        var target = new RunTarget<uint>(screen.TrueColorPixels, screen.Pitch, screen.Depth, screen.DepthWidth, screen.Viewport, screen.Format);
        DrawRuns(target, SelectPalette(map, sprite, scratch), origin, depth, rows);
    }

    /// <inheritdoc/>
    [EngineRoutine(0x00418640)]
    public override void DrawOutline(Screen screen, Map map, Sprite sprite, Vector2 shift)
    {
        if (Frames.IsEmpty || FrameOffsets.Length == 0 || IsHidden)
            return;

        int halfWidth = FrameSize.Width / 2;
        int halfHeight = FrameSize.Height / 2;
        var origin = new Vector2(sprite.Position.X - shift.X - halfWidth, sprite.Position.Y - sprite.Position.Z - shift.Y - halfHeight);
        var viewport = screen.Viewport;
        if ((int)origin.X + FrameSize.Width + OutlineMargin.Width < viewport.Left || (int)origin.X >= viewport.Right)
            return;

        if ((int)origin.Y + FrameSize.Height + OutlineMargin.Height < viewport.Top || (int)origin.Y >= viewport.Bottom)
            return;

        origin.Y += sprite.Position.Z;
        float z = sprite.Position.Z;
        if (Properties.HasFlag(ObjectProperties.Wave))
        {
            z = new Angle((byte)(map.CurrentTime >> WaveTimeShift & WaveTableMask)).Sine * TopZ + z;
        }

        int record = FrameOffsets[sprite.Frame];
        int count = MemoryMarshal.AsRef<VidFrameHeader>(Frames[record..]).ExtraCount;
        if (count == 0)
            return;

        var points = MemoryMarshal.Cast<byte, VidOutlinePoint>(Frames[(record + Unsafe.SizeOf<VidFrameHeader>())..]);
        Span<Vector2> vertices = new Vector2[count * 2 + 2];
        for (int i = 0; i < count; i++)
        {
            ref readonly var point = ref points[i];
            float pointZ = point.Z + z;
            float baseX = point.X + origin.X;
            float baseY = point.Y + origin.Y;
            vertices[i * 2] = new Vector2(baseX, baseY - pointZ);
            vertices[i * 2 + 1] = new Vector2(pointZ * OutlineSlant.X + baseX, baseY - pointZ * OutlineSlant.Y);
        }

        vertices[count * 2] = vertices[0];
        vertices[count * 2 + 1] = vertices[1];
        var graphics = map.Session.Graphics;
        graphics.SetRenderState(RenderState.SpecularEnable, 0);
        graphics.SetAlphaBlend(BlendFactor.Zero, BlendFactor.SourceColor);
        ScreenPrimitives.FillTriangleStrip(screen, vertices, OutlineShadeColor, graphics.Blend.Source, graphics.Blend.Destination, CullMode.CounterClockwise);
        graphics.SetAlphaBlend(BlendFactor.DestinationColor, BlendFactor.One);
        ScreenPrimitives.FillTriangleStrip(screen, vertices, OutlineLightColor, graphics.Blend.Source, graphics.Blend.Destination, CullMode.Clockwise);
    }

    private protected Point GetScreenOrigin(Sprite sprite, Vector2 shift) =>
        new((int)sprite.Position.X - (int)shift.X - FrameSize.Center.X, (int)(sprite.Position.Y - sprite.Position.Z) - (int)shift.Y - FrameSize.Center.Y);

    private protected bool TryLocateFrame(Map map, Sprite sprite, Rectangle viewport, ref Point origin, out int depth, out FrameRows rows)
    {
        depth = 0;
        rows = default;
        if (viewport.Left > origin.X + FrameSize.Width || origin.X >= viewport.Right || viewport.Top > origin.Y + FrameSize.Height || origin.Y >= viewport.Bottom)
            return false;

        depth = (int)(sprite.Position.Z * HeightScale);
        if (Properties.HasFlag(ObjectProperties.AlwaysTop) && depth <= LiftedLimit)
        {
            depth += LiftedDepth;
        }
        else if (Properties.HasFlag(ObjectProperties.Wave))
        {
            int bob = (int)(new Angle((byte)(map.CurrentTime >> WaveTimeShift & WaveTableMask)).Sine * TopZ * HeightScale);
            depth += bob;
            origin.Y -= bob + (bob >> 31 & 7) >> 3;
        }

        var frame = FrameRows.Read(Frames, FrameOffsets[sprite.Frame]);
        int top = frame.Top + origin.Y;
        if (top >= viewport.Bottom || viewport.Top > frame.Count + top)
            return false;

        rows = frame with { Top = top };
        return true;
    }

    private protected int GetPaletteOffset(Sprite sprite) => FileType.HasFlag(VidType.PaletteVariants) ? GetPaletteSize() * ((int)sprite.Army & ArmyMask) : 0;

    private protected ReadOnlySpan<byte> SelectPalette(Map map, Sprite sprite, Span<byte> scratch)
    {
        if (!sprite.HasExtraData || sprite.GammaOverride is { Negative: 0, Positive: 0 })
            return Palettes[GetPaletteOffset(sprite)..];

        int paletteSize = GetPaletteSize();
        int variant = FileType.HasFlag(VidType.PaletteVariants) ? paletteSize * 4 : 0;
        Palettes.Slice(variant, paletteSize).CopyTo(scratch);
        var gamma = sprite.GetGamma();
        ApplyGamma(scratch[..paletteSize], Properties.HasFlag(ObjectProperties.Gamma) ? gamma : gamma.Add(map.Session.Graphics.Gamma));
        return scratch;
    }

    private protected void DrawRuns<TColor>(in RunTarget<TColor> target, ReadOnlySpan<byte> palette, Point origin, int depth, FrameRows rows)
        where TColor : unmanaged
    {
        if (FileType.HasFlag(VidType.Blended) && FileType.HasFlag(VidType.Runs8Bit) && FileType.HasFlag(VidType.Palette))
        {
            DrawBlendedRuns(target, MemoryMarshal.Cast<byte, uint>(palette), origin, depth, rows);
        }
        else if (FileType.HasFlag(VidType.Runs8Bit) && !FileType.HasFlag(VidType.Palette))
        {
            DrawColorRuns(target, origin, depth, rows);
        }
        else if (FileType.HasFlag(VidType.Runs8Bit) && FileType.HasFlag(VidType.ZBuffer))
        {
            DrawDepthRuns(target, MemoryMarshal.Cast<byte, TColor>(palette), origin, (short)depth, rows);
        }
        else if (FileType.HasFlag(VidType.Runs8Bit))
        {
            DrawPaletteRuns(target, MemoryMarshal.Cast<byte, TColor>(palette), origin, depth, rows);
        }
    }

    private void DrawColorRuns<TColor>(in RunTarget<TColor> target, Point origin, int depth, FrameRows rows)
        where TColor : unmanaged
    {
        var runs = new RunWalker(Frames, rows, origin.X, FrameSize.Width, target.Viewport, sizeof(ushort));
        depth = Math.Min(depth + DepthBias, DepthLimit);
        int step = GetSlope(runs.RowCount, ref depth);
        while (runs.MoveNext())
        {
            if (!runs.IsVisible)
                continue;

            int rowDepth = depth + (runs.Row - runs.Top) * step;
            var pixels = MemoryMarshal.Cast<byte, ushort>(Frames[(runs.Pixels + runs.Offset * sizeof(ushort))..]);
            int depthIndex = runs.Row * target.DepthPitch + runs.First;
            int colorIndex = runs.Row * target.Pitch + runs.First;
            for (int i = 0; i < runs.Count; i++)
            {
                if (target.Depths[depthIndex + i] <= rowDepth)
                {
                    target.Depths[depthIndex + i] = (ushort)rowDepth;
                    target.Colors[colorIndex + i] = target.ToColor(pixels[i]);
                }
            }
        }
    }

    private void DrawPaletteRuns<TColor>(in RunTarget<TColor> target, ReadOnlySpan<TColor> palette, Point origin, int depth, FrameRows rows)
        where TColor : unmanaged
    {
        var runs = new RunWalker(Frames, rows, origin.X, FrameSize.Width, target.Viewport, sizeof(byte));
        depth = Math.Min(depth + DepthBias, DepthLimit);
        int step = GetSlope(runs.RowCount, ref depth);
        while (runs.MoveNext())
        {
            if (!runs.IsVisible)
                continue;

            int rowDepth = depth + (runs.Row - runs.Top) * step;
            var indexes = Frames[(runs.Pixels + runs.Offset)..];
            int depthIndex = runs.Row * target.DepthPitch + runs.First;
            int colorIndex = runs.Row * target.Pitch + runs.First;
            if (target.IsTrueColor && runs.IsClipped)
            {
                DrawPaletteRun(MemoryMarshal.Cast<TColor, uint>(target.Colors), target.Depths, MemoryMarshal.Cast<TColor, uint>(palette), indexes, depthIndex, colorIndex, runs.Count, (short)rowDepth);
                continue;
            }

            for (int i = 0; i < runs.Count; i++)
            {
                if (target.Depths[depthIndex + i] <= rowDepth)
                {
                    target.Depths[depthIndex + i] = (ushort)rowDepth;
                    target.Colors[colorIndex + i] = palette[indexes[i]];
                }
            }
        }
    }

    private void DrawDepthRuns<TColor>(in RunTarget<TColor> target, ReadOnlySpan<TColor> palette, Point origin, short key, FrameRows rows)
        where TColor : unmanaged
    {
        var runs = new RunWalker(Frames, rows, origin.X, FrameSize.Width, target.Viewport, sizeof(short) + sizeof(byte));
        while (runs.MoveNext())
        {
            if (!runs.IsVisible)
                continue;

            var offsets = MemoryMarshal.Cast<byte, short>(Frames[(runs.Pixels + runs.Offset * sizeof(short))..]);
            var indexes = Frames[(runs.Pixels + runs.Length * sizeof(short) + runs.Offset)..];
            int depthIndex = runs.Row * target.DepthPitch + runs.First;
            int colorIndex = runs.Row * target.Pitch + runs.First;
            int remaining = runs.Count;
            int i = 0;
            bool more;
            do
            {
                short value = (short)(key + offsets[i]);
                if ((short)target.Depths[depthIndex + i] < value)
                {
                    target.Depths[depthIndex + i] = (ushort)value;
                    target.Colors[colorIndex + i] = palette[indexes[i]];
                }

                i++;
                more = 0 < remaining;
                remaining--;
            }
            while (remaining != 0 && more);
        }
    }

    private void DrawBlendedRuns<TColor>(in RunTarget<TColor> target, ReadOnlySpan<uint> palette, Point origin, int depth, FrameRows rows)
        where TColor : unmanaged
    {
        var runs = new RunWalker(Frames, rows, origin.X, FrameSize.Width, target.Viewport, sizeof(byte));
        short key = (short)Math.Min(depth + DepthBias, DepthLimit);
        int step = 0;
        if (HasSlope)
        {
            key = (short)(key + (short)runs.RowCount * Cell);
            step = SlopeStep;
        }

        while (runs.MoveNext())
        {
            if (!runs.IsVisible)
                continue;

            ushort rowKey = (ushort)(short)(key + (runs.Row - runs.Top) * step);
            var indexes = Frames[(runs.Pixels + runs.Offset)..];
            int depthIndex = runs.Row * target.DepthPitch + runs.First;
            int colorIndex = runs.Row * target.Pitch + runs.First;
            if (target.IsTrueColor)
            {
                BlendRun32(MemoryMarshal.Cast<TColor, uint>(target.Colors), target.Depths, palette, indexes, depthIndex, colorIndex, runs.Count, rowKey);
            }
            else
            {
                BlendRun16(MemoryMarshal.Cast<TColor, ushort>(target.Colors), target.Depths, target.Format, palette, indexes, depthIndex, colorIndex, runs.Count, rowKey);
            }
        }
    }

    private int GetSlope(int rowCount, ref int depth)
    {
        if (!HasSlope)
            return 0;

        depth += rowCount * Cell;
        return SlopeStep;
    }
}

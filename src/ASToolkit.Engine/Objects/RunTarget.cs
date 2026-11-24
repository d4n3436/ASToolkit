using System.Drawing;
using System.Runtime.CompilerServices;

namespace ASToolkit.Engine;

internal readonly ref struct RunTarget<TColor>
    where TColor : unmanaged
{
    public RunTarget(Span<TColor> colors, int pitch, Span<ushort> depths, int depthPitch, Rectangle viewport, HighColorFormat format)
    {
        Colors = colors;
        Pitch = pitch;
        Depths = depths;
        DepthPitch = depthPitch;
        Viewport = viewport;
        Format = format;
    }

    public Span<TColor> Colors { get; }

    public int Pitch { get; }

    public Span<ushort> Depths { get; }

    public int DepthPitch { get; }

    public Rectangle Viewport { get; }

    public HighColorFormat Format { get; }

    public bool IsTrueColor => typeof(TColor) == typeof(uint);

    public TColor ToColor(ushort pixel)
    {
        if (!IsTrueColor)
            return Unsafe.As<ushort, TColor>(ref pixel);

        uint color = Format.Expand(pixel);
        return Unsafe.As<uint, TColor>(ref color);
    }
}

using System.Runtime.CompilerServices;

namespace ASToolkit.Core.Engine;

internal readonly ref struct ScreenWriter
{
    private const uint OpaqueAlpha = 0xff000000;

    private readonly Span<uint> _trueColor;
    private readonly Span<ushort> _highColor;
    private readonly HighColorFormat _format;
    private readonly bool _isTrueColor;

    public ScreenWriter(Screen screen)
    {
        _isTrueColor = screen.IsTrueColor;
        _format = screen.Format;
        if (_isTrueColor)
        {
            _trueColor = screen.TrueColorPixels;
        }
        else
        {
            _highColor = screen.HighColorPixels;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Blend(int i, uint source, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        if (_isTrueColor)
        {
            _trueColor[i] = ScreenPrimitives.Combine(source, _trueColor[i] | OpaqueAlpha, sourceFactor, destinationFactor);
            return;
        }

        _highColor[i] = _format.Pack(ScreenPrimitives.Combine(source, _format.Expand(_highColor[i]) | OpaqueAlpha, sourceFactor, destinationFactor));
    }
}

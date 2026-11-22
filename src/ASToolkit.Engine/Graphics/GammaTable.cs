using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class GammaTable
{
    private const int ChannelCount = 256;
    private const int RedShift = 16;
    private const int GreenShift = 8;

    [ThreadStatic]
    private static GammaTable? _last;

    private readonly byte[] _reds = new byte[ChannelCount];
    private readonly byte[] _greens = new byte[ChannelCount];
    private readonly byte[] _blues = new byte[ChannelCount];
    private Gamma _gamma;
    private bool _isBuilt;

    public ReadOnlySpan<byte> Reds => _reds;

    public ReadOnlySpan<byte> Greens => _greens;

    public ReadOnlySpan<byte> Blues => _blues;

    public static GammaTable Get(Gamma gamma)
    {
        var table = _last ??= new GammaTable();
        if (!table._isBuilt || table._gamma != gamma)
        {
            table.Build(gamma);
        }

        return table;
    }

    private void Build(Gamma gamma)
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            _reds[channel] = (byte)gamma.Modulate(channel, RedShift);
            _greens[channel] = (byte)gamma.Modulate(channel, GreenShift);
            _blues[channel] = (byte)gamma.Modulate(channel, 0);
        }

        _gamma = gamma;
        _isBuilt = true;
    }
}

using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class GammaTable
{
    private const int ChannelCount = 256;
    private const int RedShift = 16;
    private const int GreenShift = 8;

    private readonly byte[] _reds = new byte[ChannelCount];
    private readonly byte[] _greens = new byte[ChannelCount];
    private readonly byte[] _blues = new byte[ChannelCount];
    private Gamma _gamma;
    private bool _isBuilt;

    public ReadOnlySpan<byte> Reds => _reds;

    public ReadOnlySpan<byte> Greens => _greens;

    public ReadOnlySpan<byte> Blues => _blues;

    public GammaTable For(Gamma gamma)
    {
        if (!_isBuilt || _gamma != gamma)
        {
            Build(gamma);
        }

        return this;
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

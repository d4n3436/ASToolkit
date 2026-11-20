namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a gamma packed into the channel reductions and additions the engine applies, alpha, red, green and blue from the high byte down.
/// </summary>
/// <param name="Negative">The packed channel reductions.</param>
/// <param name="Positive">The packed channel additions.</param>
public readonly record struct Gamma(uint Negative, uint Positive)
{
    private const int ChannelBits = 8;
    private const int ColorChannels = 3;
    private const uint ScriptMagnitudes = 0x7f7f7f7f;
    private const uint ScriptEvenBits = 0xfe;
    private const uint ScriptCarry = 0x100;
    private const uint SignBit = 0x80;
    private const uint MagnitudeMask = SignBit - 1;

    /// <summary>
    /// Decodes a gamma in the script form, one byte a channel that darkens below 128 and brightens from 128.
    /// </summary>
    /// <param name="gamma">The script gamma, alpha, red, green and blue from the high byte down.</param>
    /// <returns>The packed gamma.</returns>
    [EngineRoutine(0x00439a60)]
    public static Gamma FromScript(uint gamma)
    {
        uint negative = 0, positive = 0;
        for (int shift = 0; shift < sizeof(uint) * ChannelBits; shift += ChannelBits)
        {
            uint mask = shift == 0 ? MagnitudeMask : (uint)byte.MaxValue << (shift - 1);
            if ((gamma >> shift & SignBit) == 0)
            {
                negative |= (gamma & mask) << 1;
            }
            else
            {
                positive |= (~gamma & mask) << 1;
            }
        }

        return new Gamma(negative, positive);
    }

    /// <summary>
    /// Converts the gamma to the script form.
    /// </summary>
    /// <returns>The script gamma a level script would pass for the gamma, alpha, red, green and blue from the high byte down.</returns>
    public uint ToScript()
    {
        uint gamma = Negative >> 1 & ScriptMagnitudes;
        for (int shift = 0; shift < sizeof(uint) * ChannelBits; shift += ChannelBits)
        {
            if ((Positive >> shift & byte.MaxValue) != 0)
            {
                gamma |= ((~Positive >> shift & ScriptEvenBits) | ScriptCarry) >> 1 << shift;
            }
        }

        return gamma;
    }

    internal Gamma Add(Gamma other) => new(AddBytes(Negative, other.Negative), AddBytes(Positive, other.Positive));

    internal Gamma ToGray() => new(GrayBytes(Negative), GrayBytes(Positive));

    internal int GetNegative(int shift) => (int)(Negative >> shift & byte.MaxValue);

    internal int GetPositive(int shift) => (int)(Positive >> shift & byte.MaxValue);

    internal int Modulate(int color, int shift) => Math.Min(byte.MaxValue, (color * (byte.MaxValue - GetNegative(shift)) + byte.MaxValue / 2) / byte.MaxValue + GetPositive(shift));

    internal int Apply(int color, int shift) => Math.Min(byte.MaxValue, (color * ((1 << ChannelBits) - GetNegative(shift)) >> ChannelBits) + GetPositive(shift));

    private static uint AddBytes(uint first, uint second)
    {
        uint sum = 0;
        for (int shift = 0; shift < sizeof(uint) * ChannelBits; shift += ChannelBits)
        {
            sum |= Math.Min(byte.MaxValue, (first >> shift & byte.MaxValue) + (second >> shift & byte.MaxValue)) << shift;
        }

        return sum;
    }

    private static uint GrayBytes(uint value)
    {
        uint gray = ((value >> (2 * ChannelBits) & byte.MaxValue) + (value >> ChannelBits & byte.MaxValue) + (value & byte.MaxValue) + ColorChannels / 2) / ColorChannels;
        return (value & 0xff000000) | gray << (2 * ChannelBits) | gray << ChannelBits | gray;
    }
}

using System.Runtime.Intrinsics;
namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a gamma packed into the channel reductions and additions the engine applies, alpha, red, green and blue from the high byte down.
/// </summary>
/// <param name="Negative">The packed channel reductions.</param>
/// <param name="Positive">The packed channel additions.</param>
public readonly record struct Gamma(uint Negative, uint Positive)
{
    internal const uint OpaqueAlpha = 0xff000000;
    internal const int ChannelBits = 8;
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

    [EngineRoutine(0x00414f80)]
    internal Gamma Add(Gamma other)
    {
        var sum = Vector64.AddSaturate(Vector64.Create(Negative, Positive).AsByte(), Vector64.Create(other.Negative, other.Positive).AsByte()).AsUInt32();

        return new Gamma(sum[0], sum[1]);
    }

    internal int GetNegative(int shift) => (int)(Negative >> shift & byte.MaxValue);

    internal int GetPositive(int shift) => (int)(Positive >> shift & byte.MaxValue);

    internal int GetChannel(int shift) => GetNegative(shift) != 0 ? -GetNegative(shift) : GetPositive(shift);

    internal int Modulate(int color, int shift) => Math.Min(byte.MaxValue, (color * (byte.MaxValue - GetNegative(shift)) + byte.MaxValue / 2) / byte.MaxValue + GetPositive(shift));

    internal int Apply(int color, int shift) => Math.Min(byte.MaxValue, (color * ((1 << ChannelBits) - GetNegative(shift)) >> ChannelBits) + GetPositive(shift));

    internal uint Apply(uint color) =>
        OpaqueAlpha | (uint)Apply((int)(color >> (2 * ChannelBits) & byte.MaxValue), 2 * ChannelBits) << (2 * ChannelBits) |
        (uint)Apply((int)(color >> ChannelBits & byte.MaxValue), ChannelBits) << ChannelBits | (uint)Apply((int)(color & byte.MaxValue), 0);

    [EngineRoutine(0x004432e0)]
    internal Gamma WithRed(int value) => WithChannel(value, 2 * ChannelBits);

    [EngineRoutine(0x00443340)]
    internal Gamma WithGreen(int value) => WithChannel(value, ChannelBits);

    [EngineRoutine(0x004433a0)]
    internal Gamma WithBlue(int value) => WithChannel(value, 0);

    private Gamma WithChannel(int value, int shift)
    {
        value = Math.Clamp(value, -byte.MaxValue, byte.MaxValue);
        uint keep = ~((uint)byte.MaxValue << shift);
        return value < 0
            ? new Gamma(Negative & keep | (uint)-value << shift, Positive & keep)
            : new Gamma(Negative & keep, Positive & keep | (uint)value << shift);
    }
}

using System.Runtime.Intrinsics;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a gamma packed into the channel reductions and additions the engine applies, alpha, red, green and blue from the high byte down.
/// </summary>
/// <param name="Negative">The packed channel reductions.</param>
/// <param name="Positive">The packed channel additions.</param>
public readonly record struct Gamma(uint Negative, uint Positive)
{
    /// <summary>
    /// Represents the alpha byte of a packed color that is fully opaque.
    /// </summary>
    public const uint OpaqueAlpha = 0xff000000;

    /// <summary>
    /// Represents the number of bits of each channel of a packed color.
    /// </summary>
    public const int ChannelBits = 8;


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

    /// <summary>
    /// Adds another gamma to this gamma channel by channel, each sum saturating at 255.
    /// </summary>
    /// <param name="other">The gamma to add.</param>
    /// <returns>The sum of the gammas.</returns>
    [EngineRoutine(0x00414f80)]
    public Gamma Add(Gamma other)
    {
        var sum = Vector64.AddSaturate(Vector64.Create(Negative, Positive).AsByte(), Vector64.Create(other.Negative, other.Positive).AsByte()).AsUInt32();

        return new Gamma(sum[0], sum[1]);
    }

    /// <summary>
    /// Gets the reduction of one channel.
    /// </summary>
    /// <param name="shift">The bit position of the channel in the packed values.</param>
    /// <returns>The reduction of the channel, from 0 to 255.</returns>
    public int GetNegative(int shift) => (int)(Negative >> shift & byte.MaxValue);

    /// <summary>
    /// Gets the addition of one channel.
    /// </summary>
    /// <param name="shift">The bit position of the channel in the packed values.</param>
    /// <returns>The addition of the channel, from 0 to 255.</returns>
    public int GetPositive(int shift) => (int)(Positive >> shift & byte.MaxValue);

    /// <summary>
    /// Gets one channel as a signed value, negative when the channel reduces.
    /// </summary>
    /// <param name="shift">The bit position of the channel in the packed values.</param>
    /// <returns>The channel, from -255 to 255.</returns>
    public int GetChannel(int shift) => GetNegative(shift) != 0 ? -GetNegative(shift) : GetPositive(shift);

    /// <summary>
    /// Applies one channel to a color channel, scaling by 255 with rounding.
    /// </summary>
    /// <param name="color">The color channel, from 0 to 255.</param>
    /// <param name="shift">The bit position of the channel in the packed values.</param>
    /// <returns>The adjusted color channel, from 0 to 255.</returns>
    public int Modulate(int color, int shift) => Math.Min(byte.MaxValue, (color * (byte.MaxValue - GetNegative(shift)) + byte.MaxValue / 2) / byte.MaxValue + GetPositive(shift));

    /// <summary>
    /// Applies one channel to a color channel, scaling by 256.
    /// </summary>
    /// <param name="color">The color channel, from 0 to 255.</param>
    /// <param name="shift">The bit position of the channel in the packed values.</param>
    /// <returns>The adjusted color channel, from 0 to 255.</returns>
    public int Apply(int color, int shift) => Math.Min(byte.MaxValue, (color * ((1 << ChannelBits) - GetNegative(shift)) >> ChannelBits) + GetPositive(shift));

    /// <summary>
    /// Applies the red, green and blue channels to a packed color and makes the color opaque.
    /// </summary>
    /// <param name="color">The packed color, alpha, red, green and blue from the high byte down.</param>
    /// <returns>The adjusted color.</returns>
    public uint Apply(uint color) =>
        OpaqueAlpha | (uint)Apply((int)(color >> (2 * ChannelBits) & byte.MaxValue), 2 * ChannelBits) << (2 * ChannelBits) |
        (uint)Apply((int)(color >> ChannelBits & byte.MaxValue), ChannelBits) << ChannelBits | (uint)Apply((int)(color & byte.MaxValue), 0);

    /// <summary>
    /// Returns this gamma with its red channel set to a value.
    /// </summary>
    /// <param name="value">The value of the channel, negative to reduce; clamped to the range from -255 to 255.</param>
    /// <returns>The changed gamma.</returns>
    [EngineRoutine(0x004432e0)]
    public Gamma WithRed(int value) => WithChannel(value, 2 * ChannelBits);

    /// <summary>
    /// Returns this gamma with its green channel set to a value.
    /// </summary>
    /// <param name="value">The value of the channel, negative to reduce; clamped to the range from -255 to 255.</param>
    /// <returns>The changed gamma.</returns>
    [EngineRoutine(0x00443340)]
    public Gamma WithGreen(int value) => WithChannel(value, ChannelBits);

    /// <summary>
    /// Returns this gamma with its blue channel set to a value.
    /// </summary>
    /// <param name="value">The value of the channel, negative to reduce; clamped to the range from -255 to 255.</param>
    /// <returns>The changed gamma.</returns>
    [EngineRoutine(0x004433a0)]
    public Gamma WithBlue(int value) => WithChannel(value, 0);

    private Gamma WithChannel(int value, int shift)
    {
        value = Math.Clamp(value, -byte.MaxValue, byte.MaxValue);
        uint keep = ~((uint)byte.MaxValue << shift);
        return value < 0
            ? new Gamma(Negative & keep | (uint)-value << shift, Positive & keep)
            : new Gamma(Negative & keep, Positive & keep | (uint)value << shift);
    }
}

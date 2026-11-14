using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents the negative and positive palette adjustments used by Alien Shooter.
/// </summary>
/// <param name="Negative">The packed channel reductions.</param>
/// <param name="Positive">The packed channel additions.</param>
public readonly record struct PaletteTint(uint Negative, uint Positive)
{
    private const int ChannelBits = 8;
    private const int ColorChannels = 3;
    private const int SignBit = 128;
    private const int MagnitudeMask = SignBit - 1;
    private const int PaletteScale = 2;

    /// <summary>
    /// Converts a script gamma value to palette adjustments.
    /// </summary>
    public static PaletteTint FromScriptGamma(uint gamma)
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

        return new PaletteTint(negative, positive);
    }

    /// <summary>
    /// Converts the palette adjustments to the script gamma representation.
    /// </summary>
    public uint ToScriptGamma()
    {
        uint gamma = 0;
        for (int shift = 0; shift < sizeof(uint) * ChannelBits; shift += ChannelBits)
        {
            uint positive = Positive >> shift & byte.MaxValue;
            uint channel = positive == 0 ? (Negative >> shift & byte.MaxValue) / PaletteScale
                : byte.MaxValue - positive / PaletteScale;
            gamma |= channel << shift;
        }

        return gamma;
    }

    internal static PaletteTint Pack(int red, int green, int blue, int alpha) =>
        new(GetMagnitude(-alpha) << (ColorChannels * ChannelBits) | GetMagnitude(-red) << (2 * ChannelBits) | GetMagnitude(-green) << ChannelBits | GetMagnitude(-blue),
            GetMagnitude(alpha) << (ColorChannels * ChannelBits) | GetMagnitude(red) << (2 * ChannelBits) | GetMagnitude(green) << ChannelBits | GetMagnitude(blue));

    /// <summary>
    /// Creates the tint a weapon record's keyframe animation gives at a position, in keyframes.
    /// </summary>
    public static PaletteTint FromKeyframes(in WeaponRecord weapon, float keyframe) =>
        Pack(GetKeyframeValue(weapon.RedKeys, keyframe), GetKeyframeValue(weapon.GreenKeys, keyframe), GetKeyframeValue(weapon.BlueKeys, keyframe),
            GetKeyframeValue(weapon.AlphaKeys, keyframe));

    internal PaletteTint Add(PaletteTint other) => new(AddBytes(Negative, other.Negative), AddBytes(Positive, other.Positive));

    internal PaletteTint ToGray() => new(GrayBytes(Negative), GrayBytes(Positive));

    internal int GetNegative(int shift) => (int)(Negative >> shift & byte.MaxValue);

    internal int GetPositive(int shift) => (int)(Positive >> shift & byte.MaxValue);

    internal int Modulate(int color, int shift) => Math.Min(byte.MaxValue, (color * (byte.MaxValue - GetNegative(shift)) + byte.MaxValue / 2) / byte.MaxValue + GetPositive(shift));

    internal int Apply(int color, int shift) => Math.Min(byte.MaxValue, (color * ((1 << ChannelBits) - GetNegative(shift)) >> ChannelBits) + GetPositive(shift));

    private static int GetKeyframeValue(in KeyframeValues<int> keys, float keyframe)
    {
        int key = Math.Max(0, (int)keyframe);
        if (key >= WeaponRecord.KeyframeCount - 1)
            return keys[WeaponRecord.KeyframeCount - 1];

        return (int)((keys[key + 1] - keys[key]) * (keyframe - key) + keys[key]);
    }

    private static uint GetMagnitude(int value) => (uint)Math.Clamp(value, 0, byte.MaxValue);

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

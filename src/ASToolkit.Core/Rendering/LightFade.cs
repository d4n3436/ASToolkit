namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents an Alien Shooter script's transition between two palette tints.
/// </summary>
/// <param name="StartedAt">The start time in milliseconds.</param>
/// <param name="From">The palette tint at the start.</param>
/// <param name="To">The requested palette tint.</param>
public readonly record struct LightFade(int StartedAt, PaletteTint From, PaletteTint To)
{
    private const int Duration = 1024;
    private const int StepDuration = 4;
    private const int ChannelBits = 8;
    private const int ColorChannels = 3;
    private const int SignedRange = 254;

    /// <summary>
    /// Gets the palette tint at the given time in milliseconds.
    /// </summary>
    public PaletteTint At(int time)
    {
        if (From == To || time < StartedAt)
            return From;
        
        if (time >= StartedAt + Duration)
            return To;

        int step = (time - StartedAt) / StepDuration;
        uint from = From.ToScriptGamma(), to = To.ToScriptGamma(), gamma = 0;
        for (int shift = 0; shift < ColorChannels * ChannelBits; shift += ChannelBits)
        {
            int first = Decode(from >> shift), last = Decode(to >> shift);
            gamma |= (uint)(byte)(first + (last - first) * step / byte.MaxValue) << shift;
        }
        
        return PaletteTint.FromScriptGamma(gamma);
    }

    private static int Decode(uint channel)
    {
        int value = (int)(channel & byte.MaxValue);
        return value > sbyte.MaxValue ? value - SignedRange : value;
    }
}

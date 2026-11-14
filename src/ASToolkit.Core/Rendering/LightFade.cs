using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents an Alien Shooter script's transition between two gammas.
/// </summary>
/// <param name="StartedAt">The start time in milliseconds.</param>
/// <param name="From">The palette gamma at the start.</param>
/// <param name="To">The requested palette gamma.</param>
public readonly record struct LightFade(int StartedAt, Gamma From, Gamma To)
{
    private const int Duration = 1024;
    private const int StepDuration = 4;
    private const int ChannelBits = 8;
    private const int ColorChannels = 3;
    private const int SignedRange = 254;

    /// <summary>
    /// Finds the gamma the transition reaches at a time.
    /// </summary>
    /// <param name="time">The time, in milliseconds.</param>
    /// <returns>The starting gamma before the transition starts, the requested gamma once it ends, or a blend of the two in between.</returns>
    public Gamma At(int time)
    {
        if (From == To || time < StartedAt)
            return From;
        
        if (time >= StartedAt + Duration)
            return To;

        int step = (time - StartedAt) / StepDuration;
        uint from = From.ToScript(), to = To.ToScript(), gamma = 0;
        for (int shift = 0; shift < ColorChannels * ChannelBits; shift += ChannelBits)
        {
            int first = Decode(from >> shift), last = Decode(to >> shift);
            gamma |= (uint)(byte)(first + (last - first) * step / byte.MaxValue) << shift;
        }
        
        return Gamma.FromScript(gamma);
    }

    private static int Decode(uint channel)
    {
        int value = (int)(channel & byte.MaxValue);
        return value > sbyte.MaxValue ? value - SignedRange : value;
    }
}

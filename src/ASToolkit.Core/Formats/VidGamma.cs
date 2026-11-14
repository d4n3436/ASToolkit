namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a gamma as objects and weapon keyframes store it, one signed value a channel that darkens below zero and brightens above it.
/// </summary>
/// <param name="Red">The red value, from -255 to 255.</param>
/// <param name="Green">The green value, from -255 to 255.</param>
/// <param name="Blue">The blue value, from -255 to 255.</param>
/// <param name="Alpha">The alpha value, from -255 to 255.</param>
public readonly record struct VidGamma(int Red, int Green, int Blue, int Alpha)
{
    private const int ChannelBits = 8;

    /// <summary>
    /// Packs the gamma into the channel reductions and additions the engine applies.
    /// </summary>
    /// <returns>The packed gamma.</returns>
    public Gamma Pack() => new(Join(-Alpha, -Red, -Green, -Blue), Join(Alpha, Red, Green, Blue));

    private static uint Join(int alpha, int red, int green, int blue) =>
        GetMagnitude(alpha) << (3 * ChannelBits) | GetMagnitude(red) << (2 * ChannelBits) | GetMagnitude(green) << ChannelBits | GetMagnitude(blue);

    private static uint GetMagnitude(int value) => (uint)Math.Clamp(value, 0, byte.MaxValue);
}

using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

/// <summary>
/// Represents the whole-screen flash of one frame, such as the white flash of an explosion or the red flash of the player's death, as the color its blend adds.
/// </summary>
/// <param name="Red">The red the flash adds in this frame, from 0 to 255.</param>
/// <param name="Green">The green the flash adds in this frame, from 0 to 255.</param>
/// <param name="Blue">The blue the flash adds in this frame, from 0 to 255.</param>
public readonly record struct ScreenFlashBlend(int Red, int Green, int Blue)
{
    private const int Passes = 3;
    private const uint RiseParts = 9;
    private const uint FullStrength = 256;

    /// <summary>
    /// Computes the blend of a whole-screen flash at a time.
    /// </summary>
    /// <param name="start">The time the flash started, in milliseconds.</param>
    /// <param name="duration">The length of the flash, in milliseconds.</param>
    /// <param name="color">The color of the flash, packed as 0xRRGGBB.</param>
    /// <param name="time">The time of the frame, in milliseconds.</param>
    /// <returns>The color the flash adds, or null once the flash has run out.</returns>
    [EngineRoutine(0x004311a0)]
    public static ScreenFlashBlend? At(int start, int duration, int color, int time)
    {
        uint elapsed = (uint)(time - start);
        uint length = (uint)duration;

        if (elapsed >= length)
            return null;

        uint rise = length / RiseParts;
        uint strength = elapsed < rise ? elapsed * RiseParts * FullStrength / length : (length - elapsed) * FullStrength / (length - rise);
        uint packed = (uint)color;
        uint blend = ((packed & 0xff00) * strength >> 8 & 0xff00) + ((packed & 0xff) * strength >> 8) + (packed * strength >> 8 & 0xff0000);

        return new ScreenFlashBlend((int)(blend >> 16 & 0xff), (int)(blend >> 8 & 0xff), (int)(blend & 0xff));
    }

    /// <summary>
    /// Brightens the pixels of a frame by the flash, drawn three times with a destination-color blend.
    /// </summary>
    /// <param name="pixels">The pixels of the frame.</param>
    [EngineRoutine(0x00434180)]
    public void Apply(Span<Bgra32> pixels)
    {
        for (int index = 0; index < pixels.Length; index++)
        {
            ref var pixel = ref pixels[index];
            int r = pixel.R;
            int g = pixel.G;
            int b = pixel.B;
            for (int pass = 0; pass < Passes; pass++)
            {
                r = Math.Min(byte.MaxValue, r + r * Red / byte.MaxValue);
                g = Math.Min(byte.MaxValue, g + g * Green / byte.MaxValue);
                b = Math.Min(byte.MaxValue, b + b * Blue / byte.MaxValue);
            }

            pixel = new Bgra32((byte)r, (byte)g, (byte)b, pixel.A);
        }
    }
}

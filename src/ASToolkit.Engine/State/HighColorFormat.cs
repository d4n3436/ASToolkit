using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the channel layout of the 16-bit back buffer.
/// </summary>
/// <param name="RedMask">The bits of a pixel that hold red.</param>
/// <param name="GreenMask">The bits of a pixel that hold green.</param>
/// <param name="RedShift">The shift constant of the red channel.</param>
/// <param name="GreenShift">The shift constant of the green channel.</param>
public readonly record struct HighColorFormat(int RedMask, int GreenMask, int RedShift, int GreenShift)
{
    private const int ByteBits = 8;
    private const int ShortBits = 16;
    private const int ShiftMask = 0x1f;
    private const int BlueBits = 0x1f;
    private const int BlueShift = 3;
    private const uint OpaqueBlue = 0xffe00000;
    private const uint ChannelMask = 0xff00;
    private const uint UpperMask = 0xffff0000;

    /// <summary>
    /// Gets the layout with five bits of red, six of green and five of blue.
    /// </summary>
    public static HighColorFormat Rgb565 => new(0xf800, 0x7e0, 8, 3);

    /// <summary>
    /// Gets the layout with five bits of each channel.
    /// </summary>
    public static HighColorFormat Rgb555 => new(0x7c00, 0x3e0, 7, 2);

    /// <summary>
    /// Gets a value indicating whether the layout holds five bits of green.
    /// </summary>
    public bool IsRgb555 => GreenShift == Rgb555.GreenShift;

    /// <summary>
    /// Expands a 16-bit pixel to a 32-bit color.
    /// </summary>
    /// <param name="pixel">The 16-bit pixel.</param>
    /// <returns>The color with blue in the lowest byte and an opaque top byte.</returns>
    [EngineRoutine(0x00416800)]
    public uint Expand(ushort pixel) =>
        (uint)pixel << (ShortBits - RedShift & ShiftMask) & UpperMask | (uint)pixel << (ByteBits - GreenShift & ShiftMask) & ChannelMask | (((uint)pixel & BlueBits) | OpaqueBlue) << BlueShift;

    /// <summary>
    /// Packs a 32-bit color into a 16-bit pixel.
    /// </summary>
    /// <param name="color">The color with blue in the lowest byte.</param>
    /// <returns>The 16-bit pixel.</returns>
    public ushort Pack(uint color) =>
        (ushort)((ushort)(color >> (ByteBits - GreenShift & ShiftMask)) & (ushort)GreenMask | (ushort)(color >> (ShortBits - RedShift & ShiftMask)) & (ushort)RedMask | (ushort)(color >> BlueShift) & BlueBits);
}

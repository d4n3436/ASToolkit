using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of a layer frame of a sprite file.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct VidLayerHeader
{
    /// <summary>
    /// The pixel format of the frame.
    /// </summary>
    [FieldOffset(0)]
    public VidPixelFormat Format;

    /// <summary>
    /// The width of the frame in pixels.
    /// </summary>
    [FieldOffset(4)]
    public short Width;

    /// <summary>
    /// The height of the frame in pixels.
    /// </summary>
    [FieldOffset(6)]
    public short Height;
}

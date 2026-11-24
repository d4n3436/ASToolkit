using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of a layer frame of a sprite file.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct VidLayerHeader
{
    /// <summary>
    /// The pixel format of the frame.
    /// </summary>
    [FieldOffset(0)]
    public readonly VidPixelFormat Format;

    /// <summary>
    /// The width of the frame in pixels.
    /// </summary>
    [FieldOffset(4)]
    public readonly short Width;

    /// <summary>
    /// The height of the frame in pixels.
    /// </summary>
    [FieldOffset(6)]
    public readonly short Height;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidLayerHeader"/> structure.
    /// </summary>
    /// <param name="format">The pixel format of the frame.</param>
    /// <param name="width">The width of the frame in pixels.</param>
    /// <param name="height">The height of the frame in pixels.</param>
    public VidLayerHeader(VidPixelFormat format, short width, short height)
    {
        Format = format;
        Width = width;
        Height = height;
    }
}

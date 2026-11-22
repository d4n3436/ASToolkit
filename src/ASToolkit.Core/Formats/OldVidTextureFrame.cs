using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a texture frame of a sprite file without the current version flag, which stores the fields as 16-bit values.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
public readonly struct OldVidTextureFrame
{
    /// <summary>
    /// The checksum of the texels of the texture frame.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Checksum;

    /// <summary>
    /// The index of the texture that holds the texture frame.
    /// </summary>
    [FieldOffset(4)]
    public readonly short Surface;

    /// <summary>
    /// The left edge of the texture frame in its texture.
    /// </summary>
    [FieldOffset(6)]
    public readonly short X;

    /// <summary>
    /// The top edge of the texture frame in its texture.
    /// </summary>
    [FieldOffset(8)]
    public readonly short Y;

    /// <summary>
    /// The width of the texture frame.
    /// </summary>
    [FieldOffset(10)]
    public readonly short Width;

    /// <summary>
    /// The height of the texture frame.
    /// </summary>
    [FieldOffset(12)]
    public readonly short Height;

    /// <summary>
    /// The left edge of the texture frame on the sprite frame.
    /// </summary>
    [FieldOffset(14)]
    public readonly short Left;

    /// <summary>
    /// The top edge of the texture frame on the sprite frame.
    /// </summary>
    [FieldOffset(16)]
    public readonly short Top;

    /// <summary>
    /// The index of the next texture frame of the same sprite frame, or zero for the last one.
    /// </summary>
    [FieldOffset(18)]
    public readonly short Next;

    /// <summary>
    /// Gets the area of the texture frame in its texture.
    /// </summary>
    public Rectangle Source => new(X, Y, Width, Height);

    /// <summary>
    /// Gets the position of the texture frame on the sprite frame.
    /// </summary>
    public Point Origin => new(Left, Top);
}

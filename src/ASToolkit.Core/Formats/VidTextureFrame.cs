using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents where one part of a frame of a sprite with surfaces lies in its textures.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 36)]
public readonly struct VidTextureFrame
{
    /// <summary>
    /// The index of the texture that holds the part.
    /// </summary>
    [FieldOffset(4)]
    public readonly int Surface;

    /// <summary>
    /// The left edge of the frame in the texture.
    /// </summary>
    [FieldOffset(8)]
    public readonly int X;

    /// <summary>
    /// The top edge of the frame in the texture.
    /// </summary>
    [FieldOffset(12)]
    public readonly int Y;

    /// <summary>
    /// The width of the frame.
    /// </summary>
    [FieldOffset(16)]
    public readonly int Width;

    /// <summary>
    /// The height of the frame.
    /// </summary>
    [FieldOffset(20)]
    public readonly int Height;

    /// <summary>
    /// The left edge of the frame on the sprite canvas.
    /// </summary>
    [FieldOffset(24)]
    public readonly int Left;

    /// <summary>
    /// The top edge of the frame on the sprite canvas.
    /// </summary>
    [FieldOffset(28)]
    public readonly int Top;

    /// <summary>
    /// The index of the entry holding the next part of the frame, or zero for the last part.
    /// </summary>
    [FieldOffset(32)]
    public readonly int Next;
}

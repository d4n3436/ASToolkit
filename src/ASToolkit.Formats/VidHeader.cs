using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of a sprite file.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct VidHeader
{
    /// <summary>
    /// The type flags of the sprite.
    /// </summary>
    [FieldOffset(0)]
    public readonly VidType Type;

    /// <summary>
    /// The milliseconds each frame of an animation lasts.
    /// </summary>
    [FieldOffset(2)]
    public readonly short Speed;

    /// <summary>
    /// The number of frames.
    /// </summary>
    [FieldOffset(4)]
    public readonly short Frames;

    /// <summary>
    /// The width of the canvas in pixels.
    /// </summary>
    [FieldOffset(6)]
    public readonly short Width;

    /// <summary>
    /// The height of the canvas in pixels.
    /// </summary>
    [FieldOffset(8)]
    public readonly short Height;

    /// <summary>
    /// Gets the size of the canvas, in pixels.
    /// </summary>
    public Size Size => new(Width, Height);
}

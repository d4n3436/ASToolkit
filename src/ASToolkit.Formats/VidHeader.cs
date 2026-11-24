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
    /// Initializes a new instance of the <see cref="VidHeader"/> structure.
    /// </summary>
    /// <param name="type">The type flags of the sprite.</param>
    /// <param name="speed">The milliseconds each frame of an animation lasts.</param>
    /// <param name="frames">The number of frames.</param>
    /// <param name="size">The size of the canvas, in pixels.</param>
    public VidHeader(VidType type, short speed, short frames, Size size)
    {
        Type = type;
        Speed = speed;
        Frames = frames;
        Width = (short)size.Width;
        Height = (short)size.Height;
    }

    /// <summary>
    /// Gets the size of the canvas, in pixels.
    /// </summary>
    public Size Size => new(Width, Height);
}

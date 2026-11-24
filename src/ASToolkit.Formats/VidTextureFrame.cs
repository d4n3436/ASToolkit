using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a texture frame of a sprite with surfaces, an area of one of its textures drawn at an offset on a sprite frame.
/// </summary>
/// <remarks>
/// Sprite frames bigger than a texture are drawn from several texture frames, chained by <see cref="Next"/>.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 36)]
public struct VidTextureFrame
{
    /// <summary>
    /// The checksum of the texels of the texture frame.
    /// </summary>
    [FieldOffset(0)]
    public int Checksum;

    /// <summary>
    /// The index of the texture that holds the texture frame.
    /// </summary>
    [FieldOffset(4)]
    public int Surface;

    /// <summary>
    /// The area of the texture frame in its texture.
    /// </summary>
    [FieldOffset(8)]
    public Rectangle Source;

    /// <summary>
    /// The position of the texture frame on the sprite frame.
    /// </summary>
    [FieldOffset(24)]
    public Point Origin;

    /// <summary>
    /// The index of the next texture frame of the same sprite frame, or zero for the last one.
    /// </summary>
    [FieldOffset(32)]
    public int Next;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidTextureFrame"/> structure from a texture frame of a sprite file without the current version flag.
    /// </summary>
    /// <param name="frame">The texture frame with 16-bit fields.</param>
    public VidTextureFrame(in OldVidTextureFrame frame)
    {
        Checksum = frame.Checksum;
        Surface = frame.Surface;
        Source = frame.Source;
        Origin = frame.Origin;
        Next = frame.Next;
    }
}

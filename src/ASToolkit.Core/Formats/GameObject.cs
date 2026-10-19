using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the fixed 688-byte body of an entry of objects.res, which <see cref="ObjectTable"/> keeps next to the name
/// and the sprite path of the entry.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 688)]
public readonly struct GameObject
{
    /// <summary>
    /// The engine object type.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint Type;

    /// <summary>
    /// The object flags.
    /// </summary>
    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

    /// <summary>
    /// The collision extent along x.
    /// </summary>
    [FieldOffset(16)]
    public readonly float ExtentX;

    /// <summary>
    /// The collision extent along y.
    /// </summary>
    [FieldOffset(20)]
    public readonly float ExtentY;

    /// <summary>
    /// The height.
    /// </summary>
    [FieldOffset(24)]
    public readonly float ExtentZ;

    /// <summary>
    /// The hit points.
    /// </summary>
    [FieldOffset(28)]
    public readonly int HitPoints;

    /// <summary>
    /// The number of directions the sprite has frames for. Every entry of the game has at least 1.
    /// </summary>
    [FieldOffset(112)]
    public readonly int Directions;

    /// <summary>
    /// The number of frames of the first animation in each direction.
    /// </summary>
    [FieldOffset(116)]
    public readonly int FramesPerDirection;

    /// <summary>
    /// The color shift of the red channel of the sprite's palette.
    /// </summary>
    [FieldOffset(0x294)]
    public readonly int TintRed;

    /// <summary>
    /// The color shift of the green channel of the sprite's palette.
    /// </summary>
    [FieldOffset(0x298)]
    public readonly int TintGreen;

    /// <summary>
    /// The color shift of the blue channel of the sprite's palette.
    /// </summary>
    [FieldOffset(0x29c)]
    public readonly int TintBlue;

    /// <summary>
    /// Gets the color shift applied to the sprite's palette per channel; a negative value darkens and a positive one adds.
    /// </summary>
    public (int R, int G, int B) Tint => (TintRed, TintGreen, TintBlue);

    /// <summary>
    /// Gets a value indicating whether the object stops the player.
    /// </summary>
    public bool IsSolid => Flags.HasFlag(ObjectFlags.Solid);

    /// <summary>
    /// Gets a value indicating whether the object breaks when shot.
    /// </summary>
    public bool IsBreakable => Flags.HasFlag(ObjectFlags.Breakable);

    /// <summary>
    /// Returns the sprite frame the engine shows for a direction byte, out of the given number of frames.
    /// </summary>
    public int GetFrame(byte dir, int frameCount)
    {
        if (FramesPerDirection == 0)
        {
            return 0;
        }

        int index = ((dir + 128 / Directions) & 255) * Directions >> 8;
        return Math.Min(index * FramesPerDirection, frameCount - 1);
    }
}

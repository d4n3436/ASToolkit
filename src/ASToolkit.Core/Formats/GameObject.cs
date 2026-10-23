using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Alien Shooter 2 layout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 704)]
public readonly struct GameObject
{
    private const uint ShiftedTypeMask = 0x7F;
    private const uint SharedTypeMask = ~0xFFu;

    /// <summary>
    /// Creates a new instance of the <see cref="GameObject"/> structure from an object of the Alien Shooter 1 layout.
    /// </summary>
    /// <param name="old">The object.</param>
    internal GameObject(OldGameObject old)
    {
        SpriteType = ConvertSpriteType(old.SpriteType);
        SpriteClass = old.SpriteClass;
        Flags = old.Flags;
        MoveMask = old.MoveMask;
        Size = old.Size;
        MaxHp = old.MaxHp;
        LinkOffset = old.LinkOffset;
        Link = old.Link;
        StepHeight = old.StepHeight;
        Directions = old.Directions;
        FrameCounts = old.FrameCounts;
        FrameDurations = old.FrameDurations;
        GammaRed = old.GammaRed;
        GammaGreen = old.GammaGreen;
        GammaBlue = old.GammaBlue;
        GammaAlpha = 0;
        Scale = old.Scale;
    }

    /// <summary>
    /// The sprite type of the object.
    /// </summary>
    [FieldOffset(0)]
    public readonly SpriteType SpriteType;

    /// <summary>
    /// The sprite class of the object.
    /// </summary>
    [FieldOffset(4)]
    public readonly SpriteClass SpriteClass;

    /// <summary>
    /// The object flags.
    /// </summary>
    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

    /// <summary>
    /// The collision groups of the object. Two objects collide when their masks share a bit.
    /// </summary>
    [FieldOffset(12)]
    public readonly uint MoveMask;

    /// <summary>
    /// The size of the object along x, y and the height.
    /// </summary>
    [FieldOffset(16)]
    public readonly Vector3 Size;

    /// <summary>
    /// The maximum hit points.
    /// </summary>
    [FieldOffset(28)]
    public readonly int MaxHp;

    /// <summary>
    /// The offset of the linked object from this one, as stored in the file.
    /// </summary>
    [FieldOffset(80)]
    public readonly Vector3 LinkOffset;

    /// <summary>
    /// The ID of the object the engine creates linked to this one, or zero for none.
    /// </summary>
    [FieldOffset(92)]
    public readonly KnownObject Link;

    /// <summary>
    /// The highest step the object climbs, and the height a bobbing object rises and sinks by.
    /// </summary>
    [FieldOffset(100)]
    public readonly float StepHeight;

    /// <summary>
    /// The number of directions the sprite has frames for. Every entry of the game has at least 1.
    /// </summary>
    [FieldOffset(128)]
    public readonly int Directions;

    /// <summary>
    /// The number of frames in each direction of each animation state.
    /// </summary>
    [FieldOffset(132)]
    public readonly StateValues FrameCounts;

    /// <summary>
    /// The frame duration of each animation state in milliseconds, or 0 for the speed of the sprite.
    /// </summary>
    [FieldOffset(268)]
    public readonly StateValues FrameDurations;

    /// <summary>
    /// The gamma shift of the red channel of the sprite's palette.
    /// </summary>
    [FieldOffset(676)]
    public readonly int GammaRed;

    /// <summary>
    /// The gamma shift of the green channel of the sprite's palette.
    /// </summary>
    [FieldOffset(680)]
    public readonly int GammaGreen;

    /// <summary>
    /// The gamma shift of the blue channel of the sprite's palette.
    /// </summary>
    [FieldOffset(684)]
    public readonly int GammaBlue;

    /// <summary>
    /// The gamma shift of the alpha of the sprite. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(688)]
    public readonly int GammaAlpha;

    /// <summary>
    /// The scale of the object along x, y and the height.
    /// </summary>
    [FieldOffset(692)]
    public readonly Vector3 Scale;

    /// <summary>
    /// Gets the gamma shift applied to the sprite's palette per channel.
    /// </summary>
    public (int R, int G, int B) Gamma => (GammaRed, GammaGreen, GammaBlue);

    /// <summary>
    /// Gets the number of frames of the standing animation in each direction.
    /// </summary>
    public int FramesPerDirection => FrameCounts[(int)SpriteAnimation.Stand];

    /// <summary>
    /// Gets a value indicating whether the object stops the player.
    /// </summary>
    public bool IsSolid => Flags.HasFlag(ObjectFlags.Solid);

    /// <summary>
    /// Gets a value indicating whether shots and explosions can reach the object.
    /// </summary>
    public bool IsHittable => Flags.HasFlag(ObjectFlags.Hittable);

    /// <summary>
    /// Returns the sprite frame the engine shows for a direction byte, out of the given number of frames.
    /// </summary>
    public int GetFrame(byte dir, int frameCount)
    {
        if (FramesPerDirection == 0)
        {
            return 0;
        }

        return Math.Min(GetDirection(dir) * FramesPerDirection, frameCount - 1);
    }

    /// <summary>
    /// Returns the index of the direction the engine picks for a direction byte.
    /// </summary>
    public int GetDirection(byte dir) => ((dir + 128 / Directions) & 255) * Directions >> 8;

    /// <summary>
    /// Returns the sprite frame that starts an animation state.
    /// </summary>
    public int GetStateStart(SpriteAnimation animation)
    {
        int start = 0;
        for (int previous = 0; previous < (int)animation; previous++)
        {
            start += FrameCounts[previous] * Directions;
        }

        return start;
    }

    /// <summary>
    /// Returns the frame duration of an animation state in milliseconds, given the speed of the sprite.
    /// </summary>
    public int GetFrameDuration(SpriteAnimation animation, int spriteSpeed) =>
        FrameDurations[(int)animation] == 0 ? spriteSpeed : FrameDurations[(int)animation];

    private static SpriteType ConvertSpriteType(OldSpriteType old) =>
        (SpriteType)((((uint)old & ShiftedTypeMask) << 1) | ((uint)old & SharedTypeMask));
}

using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Alien Shooter 1 layout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 688)]
internal readonly struct OldGameObject
{
    /// <summary>
    /// The sprite type of the object.
    /// </summary>
    [FieldOffset(0)]
    public readonly OldSpriteType SpriteType;

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
    /// The collision groups of the object.
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
    [FieldOffset(64)]
    public readonly Vector3 LinkOffset;

    /// <summary>
    /// The ID of the object the engine creates linked to this one, or zero for none.
    /// </summary>
    [FieldOffset(76)]
    public readonly KnownObject Link;

    /// <summary>
    /// The highest step the object climbs, and the height a bobbing object rises and sinks by.
    /// </summary>
    [FieldOffset(84)]
    public readonly float StepHeight;

    /// <summary>
    /// The deepest drop the object walks off.
    /// </summary>
    [FieldOffset(88)]
    public readonly float DropHeight;

    /// <summary>
    /// The number of directions the sprite has frames for.
    /// </summary>
    [FieldOffset(112)]
    public readonly int Directions;

    /// <summary>
    /// The number of frames in each direction of each animation state.
    /// </summary>
    [FieldOffset(116)]
    public readonly StateValues FrameCounts;

    /// <summary>
    /// The frame duration of each animation state in milliseconds, or 0 for the speed of the sprite.
    /// </summary>
    [FieldOffset(252)]
    public readonly StateValues FrameDurations;

    /// <summary>
    /// The gamma shift of the red channel of the sprite's palette.
    /// </summary>
    [FieldOffset(660)]
    public readonly int GammaRed;

    /// <summary>
    /// The gamma shift of the green channel of the sprite's palette.
    /// </summary>
    [FieldOffset(664)]
    public readonly int GammaGreen;

    /// <summary>
    /// The gamma shift of the blue channel of the sprite's palette.
    /// </summary>
    [FieldOffset(668)]
    public readonly int GammaBlue;

    /// <summary>
    /// The scale of the object along x, y and the height.
    /// </summary>
    [FieldOffset(676)]
    public readonly Vector3 Scale;
}

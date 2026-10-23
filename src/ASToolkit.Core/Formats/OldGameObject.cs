using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Alien Shooter 1 layout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 688)]
internal readonly struct OldGameObject
{
    /// <summary>
    /// The group of the object, which holds its category in the low byte and its kind in the next one.
    /// </summary>
    [FieldOffset(0)]
    public readonly uint Group;

    /// <summary>
    /// The engine behavior class of the object.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint Type;

    /// <summary>
    /// The object flags.
    /// </summary>
    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

    /// <summary>
    /// The collision groups of the object.
    /// </summary>
    [FieldOffset(12)]
    public readonly uint CollisionMask;

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
    /// The x offset of the attached object from this one.
    /// </summary>
    [FieldOffset(64)]
    public readonly float AttachedOffsetX;

    /// <summary>
    /// The y offset of the attached object from this one, as stored in the file.
    /// </summary>
    [FieldOffset(68)]
    public readonly float AttachedOffsetY;

    /// <summary>
    /// The height offset of the attached object from this one.
    /// </summary>
    [FieldOffset(72)]
    public readonly float AttachedOffsetZ;

    /// <summary>
    /// The ID of the object the engine creates attached to this one, or zero for none.
    /// </summary>
    [FieldOffset(76)]
    public readonly KnownObject AttachedObject;

    /// <summary>
    /// The highest step the object climbs, and the height a bobbing object rises and sinks by.
    /// </summary>
    [FieldOffset(84)]
    public readonly float StepHeight;

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
    /// The color shift of the red channel of the sprite's palette.
    /// </summary>
    [FieldOffset(660)]
    public readonly int TintRed;

    /// <summary>
    /// The color shift of the green channel of the sprite's palette.
    /// </summary>
    [FieldOffset(664)]
    public readonly int TintGreen;

    /// <summary>
    /// The color shift of the blue channel of the sprite's palette.
    /// </summary>
    [FieldOffset(668)]
    public readonly int TintBlue;

    /// <summary>
    /// The scale of the object along x.
    /// </summary>
    [FieldOffset(676)]
    public readonly float ScaleX;

    /// <summary>
    /// The scale of the object along y.
    /// </summary>
    [FieldOffset(680)]
    public readonly float ScaleY;

    /// <summary>
    /// The scale of the object along the height.
    /// </summary>
    [FieldOffset(684)]
    public readonly float ScaleZ;
}

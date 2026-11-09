using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Zombie Shooter 2 layout, the Alien Shooter 2 layout
/// with <see cref="Opacity"/> inserted ahead of <see cref="Scale"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 708)]
internal readonly struct ZombieShooter2GameObject
{
    [FieldOffset(0)]
    public readonly SpriteType SpriteType;

    [FieldOffset(4)]
    public readonly SpriteClass SpriteClass;

    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

    [FieldOffset(12)]
    public readonly uint MoveMask;

    [FieldOffset(16)]
    public readonly Vector3 Size;

    [FieldOffset(28)]
    public readonly int MaxHp;

    [FieldOffset(36)]
    public readonly float MaxSpeed;

    [FieldOffset(44)]
    public readonly float MaxZSpeed;

    [FieldOffset(80)]
    public readonly Vector3 LinkOffset;

    [FieldOffset(92)]
    public readonly KnownObject Link;

    /// <summary>
    /// The speed of the object along its facing as stored; the engine scales it by 0.001 on load.
    /// </summary>
    [FieldOffset(32)]
    public readonly float Speed;

    /// <summary>
    /// The index of the WEAP record of the object, or zero for none.
    /// </summary>
    [FieldOffset(60)]
    public readonly int WeaponIndex;

    /// <summary>
    /// The blast radius the object adds to its footprint when it hits something.
    /// </summary>
    [FieldOffset(64)]
    public readonly float BlastRadius;

    /// <summary>
    /// The damage the object sends to everything its blast reaches.
    /// </summary>
    [FieldOffset(68)]
    public readonly int Damage;

    /// <summary>
    /// The lifetime of the object in milliseconds; 999999 means forever.
    /// </summary>
    [FieldOffset(108)]
    public readonly int Lifetime;

    /// <summary>
    /// The x offset of the object created in each state.
    /// </summary>
    [FieldOffset(336)]
    public readonly StateFloats ChildX;

    /// <summary>
    /// The y offset of the object created in each state.
    /// </summary>
    [FieldOffset(404)]
    public readonly StateFloats ChildY;

    /// <summary>
    /// The height offset of the object created in each state.
    /// </summary>
    [FieldOffset(472)]
    public readonly StateFloats ChildHeight;

    /// <summary>
    /// The ID of the object created in each state.
    /// </summary>
    [FieldOffset(540)]
    public readonly StateValues Children;

    /// <summary>
    /// The number of objects created in each state.
    /// </summary>
    [FieldOffset(608)]
    public readonly StateValues ChildCounts;

    [FieldOffset(100)]
    public readonly float StepHeight;

    [FieldOffset(104)]
    public readonly float DropHeight;

    [FieldOffset(128)]
    public readonly int Directions;

    [FieldOffset(132)]
    public readonly StateValues FrameCounts;

    [FieldOffset(268)]
    public readonly StateValues FrameDurations;

    [FieldOffset(676)]
    public readonly int GammaRed;

    [FieldOffset(680)]
    public readonly int GammaGreen;

    [FieldOffset(684)]
    public readonly int GammaBlue;

    [FieldOffset(688)]
    public readonly int GammaAlpha;

    [FieldOffset(692)]
    public readonly int Opacity;

    [FieldOffset(696)]
    public readonly Vector3 Scale;
}

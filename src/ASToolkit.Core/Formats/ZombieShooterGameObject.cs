using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Zombie Shooter layout, the Alien Shooter 2 layout minus `VID_DAMAGE_MAX` and `push`.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 696)]
internal readonly struct ZombieShooterGameObject
{
    [FieldOffset(0)]
    public readonly OldSpriteType SpriteType;

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

    [FieldOffset(72)]
    public readonly Vector3 LinkOffset;

    [FieldOffset(84)]
    public readonly KnownObject Link;

    [FieldOffset(92)]
    public readonly float StepHeight;

    [FieldOffset(96)]
    public readonly float DropHeight;

    [FieldOffset(120)]
    public readonly int Directions;

    [FieldOffset(124)]
    public readonly StateValues FrameCounts;

    [FieldOffset(260)]
    public readonly StateValues FrameDurations;

    [FieldOffset(668)]
    public readonly int GammaRed;

    [FieldOffset(672)]
    public readonly int GammaGreen;

    [FieldOffset(676)]
    public readonly int GammaBlue;

    [FieldOffset(680)]
    public readonly int GammaAlpha;

    [FieldOffset(684)]
    public readonly Vector3 Scale;
}

using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 708)]
internal readonly struct ZombieShooter2GameObject
{
    [FieldOffset(0)]
    public readonly SpriteType SpriteType;

    [FieldOffset(4)]
    public readonly SpriteClass SpriteClass;

    [FieldOffset(8)]
    public readonly ObjectProperties Properties;

    [FieldOffset(12)]
    public readonly CollisionLayers MoveMask;

    [FieldOffset(16)]
    public readonly Vector3 Size;

    [FieldOffset(28)]
    public readonly int MaxHp;

    [FieldOffset(32)]
    public readonly float MaxSpeed;

    [FieldOffset(36)]
    public readonly float MaxInitialSpeed;

    [FieldOffset(40)]
    public readonly float MaxZSpeed;

    [FieldOffset(44)]
    public readonly float MaxInitialZSpeed;

    [FieldOffset(48)]
    public readonly float Acceleration;

    [FieldOffset(52)]
    public readonly float Deceleration;

    [FieldOffset(56)]
    public readonly float RotationPeriod;

    [FieldOffset(60)]
    public readonly int WeaponIndex;

    [FieldOffset(64)]
    public readonly float DeathRange;

    [FieldOffset(68)]
    public readonly int DeathDamage;

    [FieldOffset(72)]
    public readonly int MaxDeathDamage;

    [FieldOffset(76)]
    public readonly float Push;

    [FieldOffset(80)]
    public readonly Vector3 LinkOffset;

    [FieldOffset(92)]
    public readonly KnownObject Link;

    [FieldOffset(96)]
    public readonly float TopZ;

    [FieldOffset(100)]
    public readonly float StepHeight;

    [FieldOffset(104)]
    public readonly float DropHeight;

    [FieldOffset(108)]
    public readonly int Lifetime;

    [FieldOffset(128)]
    public readonly int Directions;

    [FieldOffset(132)]
    public readonly StateValues<int> FrameCounts;

    [FieldOffset(200)]
    public readonly StateValues<int> Sounds;

    [FieldOffset(268)]
    public readonly StateValues<int> FrameDurations;

    [FieldOffset(336)]
    public readonly StateValues<float> ChildX;

    [FieldOffset(404)]
    public readonly StateValues<float> ChildY;

    [FieldOffset(472)]
    public readonly StateValues<float> ChildZ;

    [FieldOffset(540)]
    public readonly StateValues<int> Children;

    [FieldOffset(608)]
    public readonly StateValues<int> ChildCounts;

    [FieldOffset(676)]
    public readonly VidGamma Gamma;

    [FieldOffset(692)]
    public readonly int BlackWhiteValue;

    [FieldOffset(696)]
    public readonly Vector3 Scale;
}

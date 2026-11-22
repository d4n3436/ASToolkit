using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 696)]
internal readonly struct ZombieShooterGameObject
{
    [FieldOffset(0)]
    public readonly OldSpriteType SpriteType;

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
    public readonly Vector3 LinkOffset;

    [FieldOffset(84)]
    public readonly KnownObject Link;

    [FieldOffset(88)]
    public readonly float TopZ;

    [FieldOffset(92)]
    public readonly float StepHeight;

    [FieldOffset(96)]
    public readonly float DropHeight;

    [FieldOffset(100)]
    public readonly int Lifetime;

    [FieldOffset(120)]
    public readonly int Directions;

    [FieldOffset(124)]
    public readonly StateValues<int> FrameCounts;

    [FieldOffset(192)]
    public readonly StateValues<int> Sounds;

    [FieldOffset(260)]
    public readonly StateValues<int> FrameDurations;

    [FieldOffset(328)]
    public readonly StateValues<float> ChildX;

    [FieldOffset(396)]
    public readonly StateValues<float> ChildY;

    [FieldOffset(464)]
    public readonly StateValues<float> ChildZ;

    [FieldOffset(532)]
    public readonly StateValues<int> Children;

    [FieldOffset(600)]
    public readonly StateValues<int> ChildCounts;

    [FieldOffset(668)]
    public readonly VidGamma Gamma;

    [FieldOffset(684)]
    public readonly Vector3 Scale;
}

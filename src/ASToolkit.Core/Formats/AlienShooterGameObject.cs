using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit, Size = 688)]
internal readonly struct AlienShooterGameObject
{
    [FieldOffset(0)]
    public readonly OldSpriteType SpriteType;

    [FieldOffset(4)]
    public readonly SpriteClass SpriteClass;

    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

    [FieldOffset(12)]
    public readonly CollisionLayers MoveMask;

    [FieldOffset(16)]
    public readonly Vector3 Size;

    [FieldOffset(28)]
    public readonly int MaxHp;

    [FieldOffset(32)]
    public readonly float Speed;

    [FieldOffset(36)]
    public readonly float ZSpeed;

    [FieldOffset(40)]
    public readonly float Acceleration;

    [FieldOffset(44)]
    public readonly float Deceleration;

    [FieldOffset(48)]
    public readonly float TurnPeriod;

    [FieldOffset(52)]
    public readonly int WeaponIndex;

    [FieldOffset(56)]
    public readonly float BlastRadius;

    [FieldOffset(60)]
    public readonly int Damage;

    [FieldOffset(64)]
    public readonly Vector3 LinkOffset;

    [FieldOffset(76)]
    public readonly KnownObject Link;

    [FieldOffset(80)]
    public readonly float RestHeight;

    [FieldOffset(84)]
    public readonly float StepHeight;

    [FieldOffset(88)]
    public readonly float DropHeight;

    [FieldOffset(92)]
    public readonly int Lifetime;

    [FieldOffset(112)]
    public readonly int Directions;

    [FieldOffset(116)]
    public readonly StateValues<int> FrameCounts;

    [FieldOffset(184)]
    public readonly StateValues<int> SoundIds;

    [FieldOffset(252)]
    public readonly StateValues<int> FrameDurations;

    [FieldOffset(320)]
    public readonly StateValues<float> ChildX;

    [FieldOffset(388)]
    public readonly StateValues<float> ChildY;

    [FieldOffset(456)]
    public readonly StateValues<float> ChildHeight;

    [FieldOffset(524)]
    public readonly StateValues<int> Children;

    [FieldOffset(592)]
    public readonly StateValues<int> ChildCounts;

    [FieldOffset(660)]
    public readonly int GammaRed;

    [FieldOffset(664)]
    public readonly int GammaGreen;

    [FieldOffset(668)]
    public readonly int GammaBlue;

    [FieldOffset(672)]
    public readonly int GammaAlpha;

    [FieldOffset(676)]
    public readonly Vector3 Scale;
}

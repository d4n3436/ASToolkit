using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit, Size = 612)]
internal readonly struct AlienShooterWeaponRecord
{
    [FieldOffset(0)]
    public readonly OldSpriteType Targets;

    [FieldOffset(4)]
    public readonly WeaponFlags Flags;

    [FieldOffset(20)]
    public readonly float DetectRange;

    [FieldOffset(24)]
    public readonly float BattleRange;

    [FieldOffset(28)]
    public readonly float Aim;

    [FieldOffset(32)]
    public readonly int ReloadTime;

    [FieldOffset(36)]
    public readonly int BuildTime;

    [FieldOffset(40)]
    public readonly int Ammo;

    [FieldOffset(48)]
    public readonly WeaponBehavior Behavior;

    [FieldOffset(60)]
    public readonly float MinRange;
}

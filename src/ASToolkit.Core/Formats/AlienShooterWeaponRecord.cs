using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit, Size = 612)]
internal readonly struct AlienShooterWeaponRecord
{
    [FieldOffset(4)]
    public readonly uint Flags;

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

    [FieldOffset(60)]
    public readonly float MinRange;
}

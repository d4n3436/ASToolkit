using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a record of the WEAP chunk of objects.res in the Alien Shooter 2 layout, which Zombie Shooter and Zombie Shooter 2 share.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 640)]
public readonly struct WeaponRecord
{
    internal WeaponRecord(AlienShooterWeaponRecord record)
    {
        Flags = record.Flags;
        DetectRange = record.DetectRange;
        BattleRange = record.BattleRange;
        Aim = record.Aim;
        VolleySize = 1;
        Ammo = record.Ammo;
        ReloadTime = record.ReloadTime;
        VolleyReloadTime = 0;
        BuildTime = record.BuildTime;
        MinRange = record.MinRange;
    }

    /// <summary>
    /// Gets the flags of the weapon.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint Flags;

    /// <summary>
    /// Gets the distance at which a unit with the weapon notices an enemy.
    /// </summary>
    [FieldOffset(24)]
    public readonly float DetectRange;

    /// <summary>
    /// Gets the farthest distance at which a unit with the weapon attacks.
    /// </summary>
    [FieldOffset(28)]
    public readonly float BattleRange;

    /// <summary>
    /// Gets the aim spread of the weapon.
    /// </summary>
    [FieldOffset(32)]
    public readonly float Aim;

    /// <summary>
    /// Gets the number of attacks of a volley.
    /// </summary>
    [FieldOffset(36)]
    public readonly int VolleySize;

    /// <summary>
    /// Gets the most ammunition the weapon holds.
    /// </summary>
    [FieldOffset(40)]
    public readonly int Ammo;

    /// <summary>
    /// Gets the time in milliseconds a unit waits after a volley before the next one.
    /// </summary>
    [FieldOffset(44)]
    public readonly int ReloadTime;

    /// <summary>
    /// Gets the time in milliseconds a unit waits between the attacks of a volley.
    /// </summary>
    [FieldOffset(48)]
    public readonly int VolleyReloadTime;

    /// <summary>
    /// Gets the time in milliseconds a unit with the weapon takes to build.
    /// </summary>
    [FieldOffset(52)]
    public readonly int BuildTime;

    /// <summary>
    /// Gets the closest distance at which a unit with the weapon attacks.
    /// </summary>
    [FieldOffset(88)]
    public readonly float MinRange;
}

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

    [FieldOffset(44)]
    public readonly int Army;

    [FieldOffset(48)]
    public readonly WeaponBehavior Behavior;

    [FieldOffset(56)]
    public readonly int Rank;

    [FieldOffset(60)]
    public readonly float MinRange;

    [FieldOffset(64)]
    public readonly int AnimationPeriod;

    [FieldOffset(68)]
    public readonly KeyframeValues<float> KeyTimes;

    [FieldOffset(100)]
    public readonly KeyframeValues<int> RedKeys;

    [FieldOffset(132)]
    public readonly KeyframeValues<int> GreenKeys;

    [FieldOffset(164)]
    public readonly KeyframeValues<int> BlueKeys;

    [FieldOffset(196)]
    public readonly KeyframeValues<int> AlphaKeys;

    [FieldOffset(228)]
    public readonly KeyframeValues<float> ScaleXKeys;

    [FieldOffset(260)]
    public readonly KeyframeValues<float> ScaleYKeys;

    [FieldOffset(292)]
    public readonly KeyframeValues<float> ScaleHeightKeys;

    [FieldOffset(324)]
    public readonly KeyframeValues<float> OffsetXKeys;

    [FieldOffset(356)]
    public readonly KeyframeValues<float> OffsetYKeys;

    [FieldOffset(548)]
    public readonly KeyframeValues<float> SpeedKeys;
}

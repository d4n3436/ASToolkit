using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a record of the WEAP chunk of objects.res in the Alien Shooter 2 layout, which Zombie Shooter and Zombie Shooter 2 share.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 640)]
public readonly struct WeaponRecord
{
    private const uint ShiftedTypeMask = 0x7F;
    private const uint SharedTypeMask = ~0xFFu;
    private const int NoDecisions = 999999;

    /// <summary>
    /// Represents the number of keyframes of a weapon animation.
    /// </summary>
    public const int KeyframeCount = 8;

    internal WeaponRecord(AlienShooterWeaponRecord record)
    {
        Flags = record.Flags;
        Length = record.Length;
        Weight = record.Weight;
        Power = record.Power;
        uint targets = (uint)record.Targets;
        Targets = (SpriteType)(((targets & ShiftedTypeMask) << 1) | (targets & SharedTypeMask));
        DecisionPeriod = NoDecisions;
        DetectRange = record.DetectRange;
        BattleRange = record.BattleRange;
        Aim = record.Aim;
        VolleySize = 1;
        Ammo = record.Ammo;
        ReloadTime = record.ReloadTime;
        VolleyReloadTime = 0;
        BuildTime = record.BuildTime;
        MinRange = record.MinRange;
        Army = record.Army;
        Behavior = record.Behavior;
        Rank = record.Rank;
        AnimationPeriod = record.AnimationPeriod;
        KeyTimes = record.KeyTimes;
        RedKeys = record.RedKeys;
        GreenKeys = record.GreenKeys;
        BlueKeys = record.BlueKeys;
        AlphaKeys = record.AlphaKeys;
        ScaleXKeys = record.ScaleXKeys;
        ScaleYKeys = record.ScaleYKeys;
        ScaleHeightKeys = record.ScaleHeightKeys;
        OffsetXKeys = record.OffsetXKeys;
        OffsetYKeys = record.OffsetYKeys;
        SpeedKeys = record.SpeedKeys;
    }

    /// <summary>
    /// The sprite type mask of eligible targets.
    /// </summary>
    [FieldOffset(0)]
    internal readonly SpriteType Targets;

    /// <summary>
    /// The flags of the weapon.
    /// </summary>
    [FieldOffset(4)]
    public readonly WeaponFlags Flags;

    /// <summary>
    /// The length of a train car with the weapon along its track.
    /// </summary>
    [FieldOffset(8)]
    public readonly float Length;

    /// <summary>
    /// The weight of the object, against which a pushing or pulling power is measured; 999999 for an immovable object.
    /// </summary>
    [FieldOffset(12)]
    public readonly float Weight;

    /// <summary>
    /// The power with which the object pulls a train or pushes what it runs into.
    /// </summary>
    [FieldOffset(16)]
    public readonly float Power;

    /// <summary>
    /// The time in milliseconds between the target decisions of a unit with the weapon; 999999 for none. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(20)]
    public readonly int DecisionPeriod;

    /// <summary>
    /// The distance at which a unit with the weapon notices an enemy.
    /// </summary>
    [FieldOffset(24)]
    public readonly float DetectRange;

    /// <summary>
    /// The farthest distance at which a unit with the weapon attacks.
    /// </summary>
    [FieldOffset(28)]
    public readonly float BattleRange;

    /// <summary>
    /// The aim spread of the weapon.
    /// </summary>
    [FieldOffset(32)]
    public readonly float Aim;

    /// <summary>
    /// The number of attacks of a volley. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(36)]
    public readonly int VolleySize;

    /// <summary>
    /// The most ammunition the weapon holds.
    /// </summary>
    [FieldOffset(40)]
    public readonly int Ammo;

    /// <summary>
    /// The time in milliseconds a unit waits after a volley before the next one.
    /// </summary>
    [FieldOffset(44)]
    public readonly int ReloadTime;

    /// <summary>
    /// The time in milliseconds a unit waits between the attacks of a volley. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(48)]
    public readonly int VolleyReloadTime;

    /// <summary>
    /// The time in milliseconds a unit with the weapon takes to build.
    /// </summary>
    [FieldOffset(52)]
    public readonly int BuildTime;

    /// <summary>
    /// The army given by default to objects that carry the weapon or that its attacks create.
    /// </summary>
    [FieldOffset(56)]
    public readonly Army Army;

    /// <summary>
    /// The behavior mask of a unit carrying the weapon.
    /// </summary>
    [FieldOffset(60)]
    public readonly WeaponBehavior Behavior;

    /// <summary>
    /// The rank by which an attacker prefers this weapon's carrier over an otherwise equal target.
    /// </summary>
    [FieldOffset(84)]
    public readonly int Rank;

    /// <summary>
    /// The closest distance at which a unit with the weapon attacks.
    /// </summary>
    [FieldOffset(88)]
    public readonly float MinRange;

    /// <summary>
    /// The period in milliseconds of the keyframe animation of objects with the weapon; 999999 runs it once over the death animation.
    /// </summary>
    [FieldOffset(92)]
    public readonly int AnimationPeriod;

    /// <summary>
    /// The time of each keyframe as a share of the animation period.
    /// </summary>
    [FieldOffset(96)]
    public readonly KeyframeValues<float> KeyTimes;

    /// <summary>
    /// The red gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(128)]
    public readonly KeyframeValues<int> RedKeys;

    /// <summary>
    /// The green gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(160)]
    public readonly KeyframeValues<int> GreenKeys;

    /// <summary>
    /// The blue gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(192)]
    public readonly KeyframeValues<int> BlueKeys;

    /// <summary>
    /// The alpha gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(224)]
    public readonly KeyframeValues<int> AlphaKeys;

    /// <summary>
    /// Reads the gamma of the keyframe animation at a position.
    /// </summary>
    /// <param name="keyframe">The position in the animation, in keyframes.</param>
    /// <returns>The gamma between the keyframes around the position.</returns>
    public VidGamma GetGamma(float keyframe) =>
        new(GetKeyframeValue(RedKeys, keyframe), GetKeyframeValue(GreenKeys, keyframe), GetKeyframeValue(BlueKeys, keyframe), GetKeyframeValue(AlphaKeys, keyframe));

    private static int GetKeyframeValue(in KeyframeValues<int> keys, float keyframe)
    {
        int key = Math.Max(0, (int)keyframe);
        if (key >= KeyframeCount - 1)
            return keys[KeyframeCount - 1];

        return (int)((keys[key + 1] - keys[key]) * (keyframe - key) + keys[key]);
    }

    /// <summary>
    /// The factor applied to the scale along x at each keyframe.
    /// </summary>
    [FieldOffset(256)]
    public readonly KeyframeValues<float> ScaleXKeys;

    /// <summary>
    /// The factor applied to the scale along y at each keyframe.
    /// </summary>
    [FieldOffset(288)]
    public readonly KeyframeValues<float> ScaleYKeys;

    /// <summary>
    /// The factor applied to the scale along the height at each keyframe.
    /// </summary>
    [FieldOffset(320)]
    public readonly KeyframeValues<float> ScaleHeightKeys;

    /// <summary>
    /// The horizontal screen offset in pixels at each keyframe.
    /// </summary>
    [FieldOffset(352)]
    public readonly KeyframeValues<float> OffsetXKeys;

    /// <summary>
    /// The vertical screen offset in pixels at each keyframe.
    /// </summary>
    [FieldOffset(384)]
    public readonly KeyframeValues<float> OffsetYKeys;

    /// <summary>
    /// The change of speed at each keyframe, in thousandths of the speed unit.
    /// </summary>
    [FieldOffset(576)]
    public readonly KeyframeValues<float> SpeedKeys;
}

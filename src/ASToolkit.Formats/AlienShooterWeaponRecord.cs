using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a record of the WEAP chunk of objects.res in the Alien Shooter layout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 612)]
public struct AlienShooterWeaponRecord
{
    /// <summary>
    /// The sprite type mask of eligible targets.
    /// </summary>
    [FieldOffset(0)]
    public readonly OldSpriteType Targets;

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
    /// The power with which the object pulls a train or pushes the objects it runs into.
    /// </summary>
    [FieldOffset(16)]
    public readonly float Power;

    /// <summary>
    /// The distance at which a unit with the weapon notices an enemy.
    /// </summary>
    [FieldOffset(20)]
    public float DetectRange;

    /// <summary>
    /// The maximum distance at which a unit with the weapon attacks.
    /// </summary>
    [FieldOffset(24)]
    public readonly float BattleRange;

    /// <summary>
    /// The aim spread of the weapon.
    /// </summary>
    [FieldOffset(28)]
    public float Aim;

    /// <summary>
    /// The time in milliseconds a unit waits between attacks.
    /// </summary>
    [FieldOffset(32)]
    public readonly int ReloadTime;

    /// <summary>
    /// The time in milliseconds a unit with the weapon takes to build.
    /// </summary>
    [FieldOffset(36)]
    public int BuildTime;

    /// <summary>
    /// The maximum ammunition the weapon holds; 999999 or more means unlimited.
    /// </summary>
    [FieldOffset(40)]
    public int Ammo;

    /// <summary>
    /// The army given by default to objects that carry the weapon or that its attacks create.
    /// </summary>
    [FieldOffset(44)]
    public readonly Army Army;

    /// <summary>
    /// The behavior mask of a unit carrying the weapon.
    /// </summary>
    [FieldOffset(48)]
    public readonly WeaponBehavior Behavior;

    /// <summary>
    /// The rank by which an attacker prefers this weapon's carrier over an otherwise equal target.
    /// </summary>
    [FieldOffset(56)]
    public readonly int Rank;

    /// <summary>
    /// The minimum distance at which a unit with the weapon attacks.
    /// </summary>
    [FieldOffset(60)]
    public readonly float MinRange;

    /// <summary>
    /// The period in milliseconds of the keyframe animation of objects with the weapon; 999999 runs it once over the death animation.
    /// </summary>
    [FieldOffset(64)]
    public readonly int AnimationPeriod;

    /// <summary>
    /// The time of each keyframe as a share of the animation period.
    /// </summary>
    [FieldOffset(68)]
    public readonly KeyframeValues<float> KeyTimes;

    /// <summary>
    /// The red gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(100)]
    public readonly KeyframeValues<int> RedKeys;

    /// <summary>
    /// The green gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(132)]
    public readonly KeyframeValues<int> GreenKeys;

    /// <summary>
    /// The blue gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(164)]
    public readonly KeyframeValues<int> BlueKeys;

    /// <summary>
    /// The alpha gamma at each keyframe, from -255 to 255.
    /// </summary>
    [FieldOffset(196)]
    public readonly KeyframeValues<int> AlphaKeys;

    /// <summary>
    /// The factor applied to the scale along x at each keyframe.
    /// </summary>
    [FieldOffset(228)]
    public readonly KeyframeValues<float> ScaleXKeys;

    /// <summary>
    /// The factor applied to the scale along y at each keyframe.
    /// </summary>
    [FieldOffset(260)]
    public readonly KeyframeValues<float> ScaleYKeys;

    /// <summary>
    /// The factor applied to the scale along the height at each keyframe.
    /// </summary>
    [FieldOffset(292)]
    public readonly KeyframeValues<float> ScaleHeightKeys;

    /// <summary>
    /// The horizontal screen offset in pixels at each keyframe.
    /// </summary>
    [FieldOffset(324)]
    public readonly KeyframeValues<float> OffsetXKeys;

    /// <summary>
    /// The vertical screen offset in pixels at each keyframe.
    /// </summary>
    [FieldOffset(356)]
    public readonly KeyframeValues<float> OffsetYKeys;

    /// <summary>
    /// The height offset in pixels at each keyframe.
    /// </summary>
    [FieldOffset(388)]
    public readonly KeyframeValues<float> OffsetZKeys;

    /// <summary>
    /// The angle about the x axis at each keyframe, in 256ths of a turn.
    /// </summary>
    [FieldOffset(420)]
    public readonly KeyframeValues<int> RotationXKeys;

    /// <summary>
    /// The angle about the y axis at each keyframe, in 256ths of a turn.
    /// </summary>
    [FieldOffset(452)]
    public readonly KeyframeValues<int> RotationYKeys;

    /// <summary>
    /// The rotation of the drawn image at each keyframe, as an angle in 256ths of a turn.
    /// </summary>
    [FieldOffset(484)]
    public readonly KeyframeValues<int> RotationZKeys;

    /// <summary>
    /// The change of speed at each keyframe, in thousandths of the speed unit.
    /// </summary>
    [FieldOffset(548)]
    public KeyframeValues<float> SpeedKeys;

    /// <summary>
    /// Unused, unknown meaning.
    /// </summary>
    [FieldOffset(580)]
    public KeyframeValues<float> UnknownKeys;

    /// <summary>
    /// Returns a keyframe track's value between a key and the next one.
    /// </summary>
    /// <typeparam name="T">The type of the track's values.</typeparam>
    /// <param name="keys">The keyframe track of this record.</param>
    /// <param name="key">The key.</param>
    /// <param name="position">The keyframe position.</param>
    /// <returns>The value.</returns>
    public readonly float Interpolate<T>(in KeyframeValues<T> keys, int key, float position)
        where T : unmanaged, INumberBase<T>
    {
        if (key >= WeaponRecord.KeyframeCount - 1)
            return float.CreateTruncating(keys[WeaponRecord.KeyframeCount - 1]);

        float low = float.CreateTruncating(ReadKey(keys, key));
        float high = float.CreateTruncating(ReadKey(keys, key + 1));
        return (high - low) * (position - key) + low;
    }

    private readonly T ReadKey<T>(in KeyframeValues<T> keys, int index)
        where T : unmanaged
    {
        if (index >= 0)
            return keys[index];

        var record = MemoryMarshal.AsBytes(new ReadOnlySpan<AlienShooterWeaponRecord>(in this));
        ref byte first = ref Unsafe.As<KeyframeValues<T>, byte>(ref Unsafe.AsRef(in keys));
        int offset = (int)Unsafe.ByteOffset(ref MemoryMarshal.GetReference(record), ref first) + index * Unsafe.SizeOf<T>();
        return offset < 0 ? default : MemoryMarshal.Read<T>(record[offset..]);
    }
}

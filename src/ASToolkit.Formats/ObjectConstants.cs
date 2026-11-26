using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the game-wide settings shared by every object.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 104)]
public readonly struct ObjectConstants
{
    /// <summary>
    /// The fastest edge scroll along each axis, in pixels per second.
    /// </summary>
    [FieldOffset(0)]
    public readonly Vector2 ScrollSpeed;

    /// <summary>
    /// The gravity of falling objects, in pixels per second squared.
    /// </summary>
    [FieldOffset(8)]
    public readonly float Gravitation;

    /// <summary>
    /// The second gravity, in pixels per second squared.
    /// </summary>
    [FieldOffset(12)]
    public readonly float Gravitation2;

    /// <summary>
    /// The hit points one repair step restores.
    /// </summary>
    [FieldOffset(16)]
    public readonly int RepairAmount;

    /// <summary>
    /// The number of resupply steps; each one refills the ammunition capacity divided by this.
    /// </summary>
    [FieldOffset(20)]
    public readonly int ResupplySteps;

    /// <summary>
    /// The fastest train speed, in pixels per second.
    /// </summary>
    [FieldOffset(24)]
    public readonly float MaxTrainSpeed;

    /// <summary>
    /// The slowing of a pushed object's slide.
    /// </summary>
    [FieldOffset(32)]
    public readonly float SlideDeceleration;

    /// <summary>
    /// The factor on a unit's build time in seconds.
    /// </summary>
    [FieldOffset(36)]
    public readonly int BuildTimeFactor;

    /// <summary>
    /// The time in milliseconds the cursor rests before its hint shows.
    /// </summary>
    [FieldOffset(52)]
    public readonly int HoverDelay;

    /// <summary>
    /// The hit points that sprites of object ID 104 heal each second.
    /// </summary>
    [FieldOffset(60)]
    public readonly int HpPerSecond;

    /// <summary>
    /// The script gamma of the selected unit.
    /// </summary>
    [FieldOffset(80)]
    public readonly uint SelectedGamma;

    /// <summary>
    /// The script gamma of an army 1 object under the cursor.
    /// </summary>
    [FieldOffset(84)]
    public readonly uint HoveredEnemyGamma;

    /// <summary>
    /// The script gamma of any other object under the cursor.
    /// </summary>
    [FieldOffset(88)]
    public readonly uint HoveredGamma;

    /// <summary>
    /// The speed above which a train crashes, in pixels per second.
    /// </summary>
    [FieldOffset(96)]
    public readonly float CrashSpeed;
}

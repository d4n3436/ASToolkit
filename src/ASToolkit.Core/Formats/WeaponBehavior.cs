namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the behavior mask of a unit.
/// </summary>
[Flags]
public enum WeaponBehavior : uint
{
    /// <summary>
    /// No behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// The unit attacks an enemy that stands within its weapon's range.
    /// </summary>
    AcquiresTargets = 1 << 0,

    /// <summary>
    /// The unit moves toward an enemy it sees but cannot attack yet.
    /// </summary>
    PursuesTargets = 1 << 1,

    /// <summary>
    /// Every member of the unit's group pursues targets once a member that pursues targets carries this bit.
    /// </summary>
    Careful = 1 << 2,

    /// <summary>
    /// The unit chases a target it detects even without pursuing targets, and gives up beyond 2,000 pixels.
    /// </summary>
    Haunts = 1 << 3,

    /// <summary>
    /// The unit may fire a super weapon on its own.
    /// </summary>
    FiresBigWeapons = 1 << 4
}

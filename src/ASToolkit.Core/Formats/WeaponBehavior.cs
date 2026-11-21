namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the behavior mask of a unit.
/// </summary>
[Flags]
public enum WeaponBehavior
{
    /// <summary>
    /// No behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// The unit attacks an enemy that stands within its weapon's range.
    /// </summary>
    Aggressive = 1 << 0,

    /// <summary>
    /// The unit moves toward an enemy it sees but cannot attack yet.
    /// </summary>
    Active = 1 << 1,

    /// <summary>
    /// The unit pursues an enemy only after it has been damaged.
    /// </summary>
    Careful = 1 << 2,

    /// <summary>
    /// The unit drops everything to chase an enemy it sees.
    /// </summary>
    Haunt = 1 << 3,

    /// <summary>
    /// The unit moves at random while idle, on the bit that <see cref="Haunt"/> uses.
    /// </summary>
    RandomMove = Haunt,

    /// <summary>
    /// The unit may fire a super weapon on its own.
    /// </summary>
    BigFire = 1 << 4,

    /// <summary>
    /// The unit runs from the player and from the corpses of its own kind.
    /// </summary>
    Cowardly = 1 << 5
}

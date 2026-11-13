namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the weapon flags interpreted by Alien Shooter.
/// </summary>
[Flags]
public enum WeaponFlags : uint
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The unit can attack while turning toward its target.
    /// </summary>
    AttacksWhileTurning = 1 << 0,

    /// <summary>
    /// The unit acquires targets only within its forward cone.
    /// </summary>
    ForwardTargets = 1 << 1,

    /// <summary>
    /// The unit may take an acceptable target before searching the rest.
    /// </summary>
    RandomTarget = 1 << 3,

    /// <summary>
    /// The attack emits a second pulse halfway through its animation.
    /// </summary>
    DoubleAttack = 1 << 4,

    /// <summary>
    /// The attack targets a unit directly; temporary aim points still scatter.
    /// </summary>
    NoSpread = 1 << 5,

    /// <summary>
    /// The unit can be pushed by another moving unit.
    /// </summary>
    Pushable = 1 << 6
}

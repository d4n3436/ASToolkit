namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the sprite frame a unit shows from a time of a playthrough on.
/// </summary>
/// <param name="Time">The time the frame starts showing, in milliseconds.</param>
/// <param name="Unit">The number of the unit within the playthrough.</param>
/// <param name="Frame">The index of the frame within the unit's sprite, or <see cref="Deleted"/> once the engine has deleted the unit.</param>
public readonly record struct UnitFrame(int Time, int Unit, int Frame)
{
    /// <summary>
    /// Represents the frame of a unit the engine has deleted.
    /// </summary>
    public const int Deleted = -1;
}

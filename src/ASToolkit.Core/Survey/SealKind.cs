namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies how a trigger closes a way back for good.
/// </summary>
public enum SealKind
{
    /// <summary>
    /// Rubble the trigger builds across a passage.
    /// </summary>
    Rubble,

    /// <summary>
    /// Doors a blackout trigger locks behind the player.
    /// </summary>
    DoorsClose
}

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the map state that outlives a level, kept for the whole game session.
/// </summary>
public sealed class GameSession
{
    /// <summary>
    /// Gets or sets a value indicating whether monster units may target neutral trains.
    /// </summary>
    public bool CanMonstersAttackNeutralTrains { get; set; }

    // TODO: Port the cursor's screen position
    /// <summary>
    /// Gets or sets the sprite of the mouse cursor.
    /// </summary>
    public Unit? Cursor { get; set; }
}

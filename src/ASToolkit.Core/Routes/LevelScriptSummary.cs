namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents what a level script sets up for the route.
/// </summary>
public sealed class LevelScriptSummary
{
    /// <summary>
    /// Gets a value indicating whether the doors are locked from the start.
    /// </summary>
    public bool DoorsLocked { get; init; }

    /// <summary>
    /// Gets the trigger that turns the lights off and locks the doors, or null.
    /// </summary>
    public TriggerArea? Blackout { get; init; }

    /// <summary>
    /// Gets the triggers that move the respawn point.
    /// </summary>
    public IReadOnlyList<TriggerArea> Checkpoints { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the win check counts dynamite sites.
    /// </summary>
    public bool DynamiteWin { get; init; }

    /// <summary>
    /// Gets the trigger that wins the level, or null.
    /// </summary>
    public TriggerArea? Exit { get; init; }
}

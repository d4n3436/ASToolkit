namespace ASToolkit.Analysis.Scripting;

/// <summary>
/// Represents the doors, triggers and objectives a level script sets up for the route.
/// </summary>
public sealed class LevelScriptSummary
{
    /// <summary>
    /// Gets a value indicating whether the level ends after its monsters and dynamite sites are cleared.
    /// </summary>
    public bool ClearsMonsters { get; init; }

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
    /// Gets a value indicating whether the level is won once its dynamite sites are gone.
    /// </summary>
    public bool WinsAfterSites { get; init; }

    /// <summary>
    /// Gets the trigger that wins the level, or null.
    /// </summary>
    public TriggerArea? Exit { get; init; }

    /// <summary>
    /// Gets the one-time blocks the script runs when the player reaches their areas.
    /// </summary>
    public IReadOnlyList<ScriptTrigger> Triggers { get; init; } = [];

    /// <summary>
    /// Gets the handlers the script defines for the deaths of objects.
    /// </summary>
    public IReadOnlyList<ScriptHandler> Handlers { get; init; } = [];

    /// <summary>
    /// Gets the rules that place objects while too many objects of one kind exist.
    /// </summary>
    public IReadOnlyList<ScriptCrowdRule> CrowdRules { get; init; } = [];

    /// <summary>
    /// Gets the if blocks of the level script's own code, not of its includes, with each block listed after the blocks inside it.
    /// </summary>
    public IReadOnlyList<ScriptBlock> Blocks { get; init; } = [];
}

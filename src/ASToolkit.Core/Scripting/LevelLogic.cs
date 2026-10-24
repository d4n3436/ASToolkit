namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents the script events of a level and the order its story leads the player through.
/// </summary>
public sealed class LevelLogic
{
    /// <summary>
    /// Gets the script events.
    /// </summary>
    public required IReadOnlyList<ScriptMarker> Markers { get; init; }

    /// <summary>
    /// Gets the places the quests lead the player to, in story order.
    /// </summary>
    public required IReadOnlyList<ProgressionStep> Progression { get; init; }

    /// <summary>
    /// Gets the handles of the placed sprites that the scripts open, break or remove during play, each with the <c>TimeLine</c> of
    /// the quest the opening waits for, or 0 when it waits for no quest.
    /// </summary>
    public required IReadOnlyDictionary<uint, int> Openings { get; init; }

    /// <summary>
    /// Gets the trigger areas that open sprites the player must step on first.
    /// </summary>
    public required IReadOnlyList<OpeningTrigger> Triggers { get; init; }

    /// <summary>
    /// Gets the quests that open sprites the walk may have to begin first, in story order.
    /// </summary>
    public required IReadOnlyList<QuestDetour> Detours { get; init; }

    /// <summary>
    /// Gets the key presses that move the player to another place once a quest has begun.
    /// </summary>
    public required IReadOnlyList<LevelSkip> Skips { get; init; }

    /// <summary>
    /// Gets the places where the player takes control of a unit.
    /// </summary>
    public required IReadOnlyList<UnitControl> Controls { get; init; }
}

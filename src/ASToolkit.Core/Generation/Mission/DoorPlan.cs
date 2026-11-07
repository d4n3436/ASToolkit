using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Represents the doors a building gets, such as its doors, locks and shortcuts by room pair, its start and targets in play order, its walk back
/// and the faults and fallbacks the doors stage counted.
/// </summary>
public sealed record DoorPlan
{
    /// <summary>Gets the gate style the doors were laid for.</summary>
    public required LevelStyle Gate { get; init; }

    /// <summary>Gets the rooms of each phase of the plan in play order.</summary>
    public required IReadOnlyList<IReadOnlySet<int>> Phases { get; init; }

    /// <summary>Gets the kind of the door on each room pair of the plan, the smaller room first.</summary>
    public required IReadOnlyDictionary<RoomPair, DoorKind> Doors { get; init; }

    /// <summary>Gets the room the player starts in.</summary>
    public required int Start { get; init; }

    /// <summary>Gets the target of each leg of the plan's walk, the generator or breaker rooms or the trigger and breaker first and the end last.</summary>
    public required IReadOnlyList<int> Targets { get; init; }

    /// <summary>Gets the number of key targets walked before the gate opens.</summary>
    public required int KeyCount { get; init; }

    /// <summary>Gets the room the level ends in.</summary>
    public int End => Targets[^1];

    /// <summary>Gets the walk from the plan's last key to the main gate, or null on an ungated level.</summary>
    public required WalkBack? WalkBack { get; init; }

    /// <summary>Gets the rooms of the return pass a blackout level with dynamite sites walks back over.</summary>
    public required IReadOnlyList<int> ReturnPass { get; init; }

    /// <summary>Gets the faults of the building's given progression the doors stage counted.</summary>
    public required IReadOnlyDictionary<BuildingFault, int> Faults { get; init; }
}

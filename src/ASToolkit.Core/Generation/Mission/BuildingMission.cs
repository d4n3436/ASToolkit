using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Represents the mission of a building with its doors chosen, such as its mission graph, the door tree from each room to its parent, the rooms of
/// the critical walk, the end room and every door laid.
/// </summary>
public sealed record BuildingMission
{
    /// <summary>Gets the mission graph of the building with its phases, keys, locks, flow, seals, links and room roles.</summary>
    public required MissionGraph Mission { get; init; }

    /// <summary>Gets the room each room of the building hangs from in the door tree, every room but the start.</summary>
    public required IReadOnlyDictionary<int, int> Parent { get; init; }

    /// <summary>Gets the rooms the building's critical walk passes.</summary>
    public required IReadOnlySet<int> Spine { get; init; }

    /// <summary>Gets the doors every walk from the start to the end crosses.</summary>
    public required IReadOnlySet<RoomPair> ChokeDoors { get; init; }

    /// <summary>Gets the room the building's level ends in.</summary>
    public required int End { get; init; }

    /// <summary>Gets every door of the building as the room it is walked from, the room it leads into and its part in the mission graph.</summary>
    public required IReadOnlyList<(int From, int To, MissionLinkKind Kind)> Realized { get; init; }
}

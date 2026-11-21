using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Represents the mission of a building after its doors are chosen.
/// </summary>
public sealed record BuildingMission
{
    /// <summary>Gets the mission graph of the building.</summary>
    public required MissionGraph Mission { get; init; }

    /// <summary>Gets the room that each room of the building hangs from in the door tree, for every room except the start.</summary>
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

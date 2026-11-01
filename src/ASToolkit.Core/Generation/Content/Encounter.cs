using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents a fight of an original level, the monsters standing in one room or the ones a spawner releases.
/// </summary>
/// <param name="Role">The role of the room the fight is in.</param>
/// <param name="Zone">The zone of that room.</param>
/// <param name="IsSpawned">Whether a spawner releases the monsters.</param>
/// <param name="Share">The share of the level's monster hit points.</param>
/// <param name="Progress">The share of the tour walked when the player meets the fight.</param>
/// <param name="TriggerRole">The role of the room whose trigger releases a spawned fight.</param>
/// <param name="TriggerZone">The zone of that room.</param>
/// <param name="IsTriggeredInRoom">Whether the trigger stands in the fight's own room.</param>
/// <param name="IsTriggeredNextDoor">Whether the trigger stands in a room that opens onto the fight's.</param>
public sealed record Encounter(RoomRole Role, GateZone Zone, bool IsSpawned, float Share, float Progress, RoomRole TriggerRole, GateZone TriggerZone, bool IsTriggeredInRoom,
    bool IsTriggeredNextDoor);

using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents a room of an original level with the number of objects of one kind it holds.
/// </summary>
/// <param name="Role">The role of the room.</param>
/// <param name="Zone">The gate zone of the room.</param>
/// <param name="Count">The number of objects in the room.</param>
/// <param name="Progress">The share of the tour walked before the room's first one.</param>
public sealed record ObjectiveRoom(RoomRole Role, GateZone Zone, int Count, float Progress);

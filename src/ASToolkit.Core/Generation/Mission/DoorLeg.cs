namespace ASToolkit.Core.Generation.Mission;

internal readonly record struct DoorLeg(int Source, int Target, int? Length, HashSet<int> Path, RoomGraph Graph);

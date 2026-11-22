using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Buildings;

internal sealed record OfficeLayout(List<OfficeRoom> Rooms, HashSet<RoomPair> Doors, List<int> Run, List<(UnitRect Rect, int Host)> Closets, int End);

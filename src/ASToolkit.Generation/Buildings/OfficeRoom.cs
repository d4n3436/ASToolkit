using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Rooms;

namespace ASToolkit.Generation.Buildings;

internal sealed record OfficeRoom(HashSet<Cell> Cells, RoomType Type, int Phase);

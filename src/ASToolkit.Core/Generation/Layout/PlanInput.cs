using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Layout;

internal sealed class PlanInput
{
    public required Dictionary<int, HashSet<Cell>> Cells { get; init; }

    public required Dictionary<int, int> PhaseOf { get; init; }

    public required Dictionary<int, int> Parent { get; init; }

    public required HashSet<int> Spine { get; init; }

    public required HashSet<int> Halls { get; init; }

    public required Dictionary<int, RoomType> Types { get; init; }

    public required int Start { get; init; }

    public required int End { get; init; }

    public required IReadOnlyList<(int From, int To, MissionLinkKind Kind)> Realized { get; init; }

    public required IReadOnlyList<(HashSet<Cell> Cells, RegionKind Kind, RoomType Type, int Host)> SideRooms { get; init; }
}

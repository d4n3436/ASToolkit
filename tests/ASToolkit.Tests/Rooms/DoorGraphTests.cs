using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Rooms;

public class DoorGraphTests
{
    [Fact]
    public void ComputeShapeHash_RenumberedGraph_MatchesAndOtherShapeDiffers()
    {
        var path = Create((1, 2), (2, 3), (3, 4), (4, 5));
        var renumbered = Create((9, 7), (7, 8), (8, 6), (6, 5));
        var star = Create((1, 2), (1, 3), (1, 4), (1, 5));

        Assert.Equal(path.ComputeShapeHash(), renumbered.ComputeShapeHash());
        Assert.NotEqual(path.ComputeShapeHash(), star.ComputeShapeHash());
        Assert.Equal([1, 1, 2, 2, 2], path.GetDegrees());
    }

    [Fact]
    public void CountLoops_RingWithTail_CountsOneLoop()
    {
        var graph = Create((1, 2), (2, 3), (3, 1), (3, 4));

        Assert.Equal(1, graph.CountLoops());
        Assert.Equal(2, graph.FindBlocks().Count);
        Assert.Equal(0, graph.GetSubgraph(new HashSet<int> { 1, 2, 4 }).CountLoops());
    }

    private static DoorGraph Create(params (int A, int B)[] doors)
    {
        int[] rooms = doors.SelectMany(door => new[] { door.A, door.B }).Distinct().Order().ToArray();
        var neighbors = rooms.ToDictionary(room => room, _ => new HashSet<int>());
        foreach ((int a, int b) in doors)
        {
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }

        return new DoorGraph(rooms, neighbors);
    }
}

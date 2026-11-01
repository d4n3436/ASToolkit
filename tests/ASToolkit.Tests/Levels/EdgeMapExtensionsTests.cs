using ASToolkit.Core.Levels;
using Xunit;

namespace ASToolkit.Tests.Levels;

public class EdgeMapExtensionsTests
{
    private static readonly HashSet<Cell> Room = Enumerable.Range(0, 3).SelectMany(u => Enumerable.Range(0, 3).Select(v => new Cell(u, v))).ToHashSet();

    [Fact]
    public void GetWalledSides_RoomCells_ReadTheirWalls()
    {
        var edges = CreateWalls();

        Assert.Equal(WallSides.LowerBack | WallSides.UpperSlash, edges.GetWalledSides(new Cell(0, 0), Room.Contains, static kind => kind.IsClosed));
        Assert.Equal(WallSides.None, edges.GetWalledSides(new Cell(1, 1), Room.Contains, static kind => kind.IsClosed));
        Assert.Equal(WallSides.UpperBack | WallSides.LowerSlash, edges.GetWalledSides(new Cell(2, 2), Room.Contains, static kind => kind.IsClosed));
    }

    [Fact]
    public void GetWalledSides_NoEdges_CountsTheFloorBorder()
    {
        var edges = new Dictionary<EdgeKey, EdgeKind>();

        Assert.Equal(WallSides.UpperBack | WallSides.UpperSlash, edges.GetWalledSides(new Cell(2, 0), Room.Contains, static kind => kind.IsClosed));
    }

    [Fact]
    public void FindNearestWall_PointNearALowerWall_ReturnsItsSideAndDistance()
    {
        var wall = CreateWalls().FindNearestWall(0.2f, 1.5f);

        Assert.NotNull(wall);
        Assert.Equal(WallSides.LowerBack, wall.Value.Side);
        Assert.Equal(0.2f, wall.Value.Distance, 3);
    }

    [Fact]
    public void FindNearestWall_DoorwayOnly_ReturnsNull()
    {
        var edges = new Dictionary<EdgeKey, EdgeKind> { [new EdgeKey('u', 0, 1)] = EdgeKind.Doorway };

        Assert.Null(edges.FindNearestWall(0.2f, 1.5f));
        Assert.NotNull(edges.FindNearestEdge(0.2f, 1.5f));
    }

    [Fact]
    public void MeasureClearance_RoomMiddle_IsHalfTheRoom()
    {
        Assert.Equal(1.5f, CreateWalls().MeasureClearance(1.5f, 1.5f), 3);
        Assert.Equal(float.PositiveInfinity, new Dictionary<EdgeKey, EdgeKind>().MeasureClearance(1.5f, 1.5f));
    }

    [Fact]
    public void CastRay_TowardADoor_ReturnsTheDoor()
    {
        var edges = CreateWalls();
        edges[new EdgeKey('u', 3, 1)] = EdgeKind.Door;

        var hit = edges.CastRay(0.5f, 1.5f, WallSides.UpperBack, Room.Contains, 5);

        Assert.NotNull(hit);
        Assert.Equal(2.5f, hit.Value.Distance, 3);
        Assert.Equal(new EdgeKey('u', 3, 1), hit.Value.Edge);
        Assert.Equal(EdgeKind.Door, hit.Value.Kind);
    }

    [Fact]
    public void CastRay_BareBorder_ReturnsNoKind()
    {
        var hit = new Dictionary<EdgeKey, EdgeKind>().CastRay(1.5f, 0.5f, WallSides.LowerSlash, Room.Contains, 5);

        Assert.NotNull(hit);
        Assert.Equal(2.5f, hit.Value.Distance, 3);
        Assert.Equal(EdgeKind.None, hit.Value.Kind);
        Assert.Null(new Dictionary<EdgeKey, EdgeKind>().CastRay(1.5f, 0.5f, WallSides.LowerSlash, cell => true, 5));
    }

    [Fact]
    public void Flood_WallAcrossTheRoom_StopsAtIt()
    {
        var edges = Enumerable.Range(0, 3).ToDictionary(v => new EdgeKey('u', 1, v), _ => EdgeKind.Wall);

        var reached = Room.Flood([new Cell(0, 0)], edge => edges.TryGetValue(edge, out var kind) && kind.BlocksWalking);

        Assert.Equal(3, reached.Count);
        Assert.All(reached, cell => Assert.Equal(0, cell.U));
    }

    [Theory]
    [InlineData(EdgeKind.Wall, true, true, true)]
    [InlineData(EdgeKind.ShutDoor, true, true, false)]
    [InlineData(EdgeKind.SecretWall, false, true, true)]
    [InlineData(EdgeKind.SpawnWall, false, false, true)]
    [InlineData(EdgeKind.Door, false, false, false)]
    [InlineData(EdgeKind.Doorway, false, false, false)]
    [InlineData(EdgeKind.BrokenWall, false, false, false)]
    public void EdgeKind_Classes_MatchTheirKinds(EdgeKind kind, bool blocksWalking, bool isClosed, bool showsWall)
    {
        Assert.Equal(blocksWalking, kind.BlocksWalking);
        Assert.Equal(isClosed, kind.IsClosed);
        Assert.Equal(showsWall, kind.ShowsWall);
    }

    [Theory]
    [InlineData(WallSides.UpperBack | WallSides.UpperSlash, true)]
    [InlineData(WallSides.LowerBack | WallSides.LowerSlash, true)]
    [InlineData(WallSides.UpperSlash | WallSides.LowerBack, true)]
    [InlineData(WallSides.UpperBack | WallSides.LowerBack, false)]
    [InlineData(WallSides.UpperBack, false)]
    [InlineData(WallSides.None, false)]
    public void IsCorner_Sides_TellMeetingWalls(WallSides sides, bool isCorner)
    {
        Assert.Equal(isCorner, sides.IsCorner);
    }

    private static Dictionary<EdgeKey, EdgeKind> CreateWalls()
    {
        var edges = new Dictionary<EdgeKey, EdgeKind>();
        for (int index = 0; index < 3; index++)
        {
            edges[new EdgeKey('u', 0, index)] = EdgeKind.Wall;
            edges[new EdgeKey('u', 3, index)] = EdgeKind.Wall;
            edges[new EdgeKey('v', index, 0)] = EdgeKind.Wall;
            edges[new EdgeKey('v', index, 3)] = EdgeKind.Wall;
        }

        return edges;
    }
}

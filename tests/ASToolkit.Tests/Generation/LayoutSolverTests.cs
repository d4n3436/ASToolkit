using System.Drawing;
using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Levels;
using Xunit;

namespace ASToolkit.Tests.Generation;

public sealed class LayoutSolverTests
{
    private static readonly DoorRule Doors = new(3, 1);

    [Fact]
    public void Solve_DoorSides_PutsTheNeighborOnTheGivenSide()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 8, 8, [ShapeTurn.Identity]);
            AddBox(problem, 1, 6, 6);
            problem.Join(0, 1);
            problem.DoorSides[(0, 1)] = DoorSides.MaxX;

            var boxes = Solve(problem, seed);

            Assert.True(boxes[1].Left >= boxes[0].Right);
        }
    }

    [Fact]
    public void Solve_TouchLengths_SharesAWallOfTheGivenLength()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 10, 6);
            AddBox(problem, 1, 6, 6);
            AddBox(problem, 2, 6, 6);
            problem.Join(0, 1);
            problem.Join(0, 2);
            problem.TouchPairs.Add((1, 2));
            problem.TouchLengths[(1, 2)] = 5;

            var boxes = Solve(problem, seed);

            Assert.True(SharedWall(boxes[1], boxes[2]) >= 5);
        }
    }

    [Fact]
    public void Solve_MayTouchPairs_NeverStandOneUnitApart()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 10, 6);
            AddBox(problem, 1, 6, 5);
            AddBox(problem, 2, 6, 5);
            problem.Join(0, 1);
            problem.Join(0, 2);
            problem.MayTouchPairs.Add((1, 2));

            var boxes = Solve(problem, seed);

            Assert.NotEqual(1, Gap(boxes[1], boxes[2]));
        }
    }

    [Fact]
    public void Solve_RingWithTwoTreeRooms_FindsALayout()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 14, 4);
            AddBox(problem, 1, 5, 10);
            AddBox(problem, 2, 14, 4);
            AddBox(problem, 3, 5, 10);
            AddBox(problem, 4, 6, 6);
            AddBox(problem, 5, 6, 6);
            problem.Join(0, 1);
            problem.Join(1, 2);
            problem.Join(2, 3);
            problem.Join(3, 0);
            problem.Join(1, 4);
            problem.Join(3, 5);

            Assert.Equal(6, Solve(problem, seed).Count);
        }
    }

    [Fact]
    public void Solve_BeyondPairs_StandPastTheAnchorsDoorEnd()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 8, 8, [ShapeTurn.Identity]);
            AddBox(problem, 1, 6, 6);
            AddBox(problem, 2, 6, 6);
            problem.Join(0, 1);
            problem.Join(1, 2);
            problem.DoorSides[(0, 1)] = DoorSides.MaxX;
            problem.BeyondPairs.Add((0, 1));
            problem.BeyondPairs.Add((0, 2));

            var boxes = Solve(problem, seed);

            Assert.True(boxes[1].Left >= boxes[0].Right && boxes[2].Left >= boxes[0].Right);
        }
    }

    [Fact]
    public void Solve_Obstacles_KeepTheMinimumDistance()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 8, 8, [ShapeTurn.Identity]);
            AddBox(problem, 1, 6, 6);
            AddBox(problem, 2, 6, 6);
            problem.Join(0, 1);
            problem.Join(1, 2);
            problem.DoorSides[(0, 1)] = DoorSides.MaxX;
            problem.BeyondPairs.Add((0, 1));
            problem.BeyondPairs.Add((0, 2));
            problem.Obstacles.Add((0, [new UnitRect(8, 8, 20, 20)]));

            var layout = new LayoutSolver<int>(problem, seed).Solve();

            Assert.NotNull(layout);
            var anchor = BoundsOf(layout[0].Cells);
            var obstacle = new Rectangle(anchor.Left + 8, anchor.Top + 8, 20, 20);
            Assert.All(layout.Values.Where(room => room.Room != 0), room => Assert.True(Gap(BoundsOf(room.Cells), obstacle) >= 2));
        }
    }

    [Fact]
    public void Solve_Corridors_KeepTheMinimumDistance()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 8, 6);
            AddBox(problem, 1, 6, 6);
            AddBox(problem, 3, 6, 6);
            problem.AddRoom(2, [new RoomShape(RoomShape.Rectangle(3, 4), [new ShapeDoor(new Point(0, 0), new Point(3, 0)), new ShapeDoor(new Point(0, 4), new Point(3, 4))],
                [ShapeTurn.Identity, ShapeTurn.Rotate90])], isCorridor: true);
            problem.Join(0, 2);
            problem.Join(2, 1);
            problem.Join(0, 3);

            var boxes = Solve(problem, seed);

            Assert.True(Gap(boxes[2], boxes[3]) >= 2);
        }
    }

    [Fact]
    public void Solve_CorridorBetweenNotchedRooms_FindsALayout()
    {
        var problem = CreateProblem();
        var doors = new DoorRule(2, 0);
        problem.AddRoom(0, [new RoomShape(Notched(9, 13, 3, 4), doors, RoomShape.AllTurns)]);
        problem.AddRoom(2, [new RoomShape(Notched(11, 11, 6, 2), doors, RoomShape.AllTurns)]);
        var ends = new[] { 0, 6 }.SelectMany(x => new[] { 0, 1 }.Select(y => new ShapeDoor(new Point(x, y), new Point(x, y + 2)))).ToList();
        problem.AddRoom(1, [new RoomShape(RoomShape.Rectangle(6, 3), ends, [ShapeTurn.Identity])], isCorridor: true);
        problem.Join(0, 1);
        problem.Join(1, 2);

        Assert.Equal(3, Solve(problem, 1).Count);
    }

    [Fact]
    public void Solve_Joins_StandOnADoorOfEachRoom()
    {
        foreach (int seed in Enumerable.Range(1, 5))
        {
            var problem = CreateProblem();
            AddBox(problem, 0, 9, 7);
            AddBox(problem, 1, 6, 5);
            problem.Join(0, 1);

            var boxes = Solve(problem, seed);

            Assert.True(SharedWall(boxes[0], boxes[1]) >= 3);
        }
    }

    private static LayoutProblem<int> CreateProblem() => new() { MinimumDistance = 2, TouchLength = 2 };

    private static void AddBox(LayoutProblem<int> problem, int room, int width, int height, IReadOnlyList<ShapeTurn>? turns = null) =>
        problem.AddRoom(room, [new RoomShape(RoomShape.Rectangle(width, height), Doors, turns ?? RoomShape.AllTurns)]);

    private static HashSet<Cell> Notched(int width, int height, int offset, int depth) =>
        RoomShape.Rectangle(width, height).Where(cell => !(cell.U >= offset && cell.U < offset + 3 && cell.V < depth)).ToHashSet();

    private static Dictionary<int, Rectangle> Solve(LayoutProblem<int> problem, int seed)
    {
        var layout = new LayoutSolver<int>(problem, seed).Solve();
        Assert.NotNull(layout);
        return layout.ToDictionary(room => room.Key, room => BoundsOf(room.Value.Cells));
    }

    private static Rectangle BoundsOf(HashSet<Cell> cells)
    {
        int left = cells.Min(cell => cell.U), top = cells.Min(cell => cell.V);
        return new Rectangle(left, top, cells.Max(cell => cell.U) + 1 - left, cells.Max(cell => cell.V) + 1 - top);
    }

    private static int SharedWall(Rectangle a, Rectangle b)
    {
        int alongX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        int alongY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return a.Right == b.Left || b.Right == a.Left ? alongY : a.Bottom == b.Top || b.Bottom == a.Top ? alongX : 0;
    }

    private static int Gap(Rectangle a, Rectangle b) =>
        Math.Max(Math.Max(b.Left - a.Right, a.Left - b.Right), Math.Max(b.Top - a.Bottom, a.Top - b.Bottom));
}

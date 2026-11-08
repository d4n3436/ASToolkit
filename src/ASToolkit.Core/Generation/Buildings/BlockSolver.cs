using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

internal sealed class BlockSolver
{
    private readonly GrammarRandom _random;

    public BlockSolver(GrammarRandom random)
    {
        _random = random;
    }

    public Dictionary<string, PlacedRoom<string>> Solve(LayoutProblem<string> problem) => new LayoutSolver<string>(problem, _random.Inclusive(1, 1_000_000)).Solve();

    // Solves one room against rooms held as one block, the block opening only at the given door segments and the room the given distance clear of
    // the banned units or standing past the door end, and returns the room's cells
    public HashSet<Cell> SolveAgainst(HashSet<Cell> block, IReadOnlyList<ShapeDoor> openings, IReadOnlyList<RoomShape> shapes, HashSet<Cell> banned, bool isBeyond, int distance)
    {
        if (openings.Count == 0)
        {
            return [];
        }

        // Add the block first to pin it where it stands
        var problem = new LayoutProblem<string> { MinimumDistance = distance };
        problem.AddRoom("block", [new RoomShape(block, openings, [ShapeTurn.Identity])]);
        problem.AddRoom("room", shapes);
        problem.Join("room", "block");
        if (isBeyond)
        {
            problem.DoorSides[("room", "block")] = DoorSides.MaxY;
            problem.BeyondPairs.Add(("room", "block"));
        }
        else if (banned.Count > 0)
        {
            problem.ClearPairs.Add(("block", "room"));
            problem.Obstacles.Add(("block", banned.ToRects()));
        }

        var layout = Solve(problem);
        if (layout.Count == 0)
        {
            return [];
        }

        var placedBlock = layout["block"].Cells;
        (int du, int dv) = (block.Min(cell => cell.U) - placedBlock.Min(cell => cell.U), block.Min(cell => cell.V) - placedBlock.Min(cell => cell.V));
        return layout["room"].Cells.Select(cell => new Cell(cell.U + du, cell.V + dv)).ToHashSet();
    }
}

using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rooms;

internal sealed class PartFinder
{
    private const int WallCells = 2;

    private static readonly HashSet<KnownObject> Scattered = [KnownObject.ScatteredPaper, KnownObject.SoldierCorpse, KnownObject.ScientistCorpse];

    private static readonly Dictionary<(PieceFamily, PieceFamily), float> Reaches = new()
    {
        [(PieceFamily.Table, PieceFamily.Chair)] = 70,
        [(PieceFamily.Table, PieceFamily.Computer)] = 45,
        [(PieceFamily.Table, PieceFamily.Labware)] = 50,
        [(PieceFamily.Table, PieceFamily.Table)] = 60,
        [(PieceFamily.Cabinet, PieceFamily.Cabinet)] = 60,
        [(PieceFamily.Cabinet, PieceFamily.Shelf)] = 80,
        [(PieceFamily.Cabinet, PieceFamily.Water)] = 60,
        [(PieceFamily.Shelf, PieceFamily.Shelf)] = 100,
        [(PieceFamily.Crate, PieceFamily.Crate)] = 55,
        [(PieceFamily.Crate, PieceFamily.Boxes)] = 55,
        [(PieceFamily.Boxes, PieceFamily.Boxes)] = 55,
        [(PieceFamily.Crate, PieceFamily.Container)] = 60,
        [(PieceFamily.Crate, PieceFamily.Truck)] = 70,
        [(PieceFamily.Container, PieceFamily.Container)] = 90,
        [(PieceFamily.Bank, PieceFamily.Bank)] = 150,
        [(PieceFamily.Console, PieceFamily.Console)] = 70,
        [(PieceFamily.Console, PieceFamily.Chair)] = 60,
        [(PieceFamily.Barrel, PieceFamily.Barrel)] = 45,
        [(PieceFamily.Barrel, PieceFamily.Cylinder)] = 45,
        [(PieceFamily.Cylinder, PieceFamily.Cylinder)] = 45,
        [(PieceFamily.Labware, PieceFamily.Labware)] = 45,
        [(PieceFamily.Machine, PieceFamily.Machine)] = 60
    };

    private readonly Lattice _lattice;
    private readonly IReadOnlyDictionary<EdgeKey, EdgeKind> _edges;

    public PartFinder(Lattice lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
    {
        _lattice = lattice;
        _edges = edges;
    }

    public (IReadOnlyList<RoomPart> Parts, IReadOnlyDictionary<KnownObject, int> Scatter) Find((string Map, int Place) room, IReadOnlyList<Cell> cells, IReadOnlyList<MergedMapSprite> pieces)
    {
        var scatter = pieces.Where(piece => Scattered.Contains(piece.Id)).GroupBy(piece => piece.Id).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count());
        var furniture = pieces.Select(piece => (Piece: piece, Family: piece.Id.Family)).Where(entry => entry.Family is not null).ToArray();
        int[] root = Enumerable.Range(0, furniture.Length).ToArray();
        for (int index = 0; index < furniture.Length; index++)
        {
            for (int other = index + 1; other < furniture.Length; other++)
            {
                float reach = GetReach(furniture[index].Family!.Value, furniture[other].Family!.Value);
                if (reach > 0 && GetDistance(furniture[index].Piece, furniture[other].Piece) <= reach)
                {
                    root[FindRoot(root, index)] = FindRoot(root, other);
                }
            }
        }

        var set = cells.ToHashSet();
        var acrossU = MeasureWallDistance(set, 'u');
        var acrossV = MeasureWallDistance(set, 'v');
        var parts = new List<RoomPart>();
        foreach (var group in Enumerable.Range(0, furniture.Length).GroupBy(index => FindRoot(root, index)))
        {
            var members = group.Select(index => furniture[index]).OrderBy(entry => entry.Piece.Position.X).ThenBy(entry => entry.Piece.Position.Y).ToArray();
            var spots = members.Select(entry => GetSpot(entry.Piece, acrossU, acrossV)).ToArray();
            var first = members[0].Piece.Position;
            parts.Add(new RoomPart
            {
                Room = room,
                Origin = new PointF(first.X, first.Y),
                Spot = spots.All(spot => spot == PartSpot.Corner) ? PartSpot.Corner : spots.Count(spot => spot != PartSpot.Center) * 2 >= spots.Length ? PartSpot.Wall : PartSpot.Center,
                Families = members.Select(entry => entry.Family!.Value).Distinct().Order().ToArray(),
                Pieces = members.Select(entry => new PartPiece(entry.Piece.Id, entry.Piece.Position.X - first.X, entry.Piece.Position.Y - first.Y, entry.Piece.Position.Z,
                    entry.Piece.Facing, entry.Piece.Payload, WallGaps.Measure(_lattice, set, _edges, entry.Piece.Position.X, entry.Piece.Position.Y))).ToArray()
            });
        }

        return (parts, scatter);
    }

    private static float GetReach(PieceFamily one, PieceFamily other) =>
        Reaches.TryGetValue((one, other), out float reach) || Reaches.TryGetValue((other, one), out reach) ? reach : 0;

    private static float GetDistance(MergedMapSprite one, MergedMapSprite other) =>
        Vector2.Distance(new Vector2(one.Position.X, one.Position.Y), new Vector2(other.Position.X, other.Position.Y));

    private static int FindRoot(int[] root, int index)
    {
        while (root[index] != index)
        {
            root[index] = root[root[index]];
            index = root[index];
        }

        return index;
    }

    private PartSpot GetSpot(MergedMapSprite piece, Dictionary<Cell, int> acrossU, Dictionary<Cell, int> acrossV)
    {
        var cell = _lattice.GetCell(piece.Position.X, piece.Position.Y);
        bool nearU = acrossU.GetValueOrDefault(cell, int.MaxValue) <= WallCells;
        bool nearV = acrossV.GetValueOrDefault(cell, int.MaxValue) <= WallCells;
        return nearU && nearV ? PartSpot.Corner : nearU || nearV ? PartSpot.Wall : PartSpot.Center;
    }

    // Count the cells to the nearest wall or edge of the room crossed along one axis
    private Dictionary<Cell, int> MeasureWallDistance(HashSet<Cell> cells, char axis)
    {
        var distance = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var cell in cells)
        {
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (edge.Axis == axis && (!cells.Contains(neighbor) || _edges.ContainsKey(edge)) && distance.TryAdd(cell, 0))
                {
                    queue.Enqueue(cell);
                }
            }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (cells.Contains(neighbor) && !_edges.ContainsKey(edge) && distance.TryAdd(neighbor, distance[cell] + 1))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return distance;
    }
}

using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Provides the dressing of a generated level from a <see cref="DressingLibrary"/>.
/// </summary>
public static class Dresser
{
    /// <summary>
    /// Covers the cells with copied windows of the objects the level holds, largest first and each source cell once, and drops objects
    /// whose support was not copied. Windows whose solid pieces stay off the cells to keep clear come first.
    /// </summary>
    public static PlacedDecor[] Dress(Random rng, DressingLibrary library, IReadOnlySet<Cell> cells, IReadOnlyDictionary<EdgeKey, EdgeKind> edges,
        IReadOnlySet<Cell> keepClear, Lattice lattice, LevelOccupancy occupancy, IReadOnlySet<KnownObject> held)
    {
        var covered = new HashSet<Cell>();
        var used = new HashSet<(int, Cell)>();
        var exhausted = new HashSet<(int, string)>();
        var placed = new List<PlacedDecor>();
        foreach (var corner in cells.OrderBy(c => c.U).ThenBy(c => c.V))
        {
            if (covered.Contains(corner))
            {
                continue;
            }

            foreach (int windowSize in library.WindowSizes)
            {
                if (!DressingLibrary.IsWindowInside(cells, corner, windowSize) || new UnitRect(corner.U, corner.V, windowSize, windowSize).Units.Any(covered.Contains)
                    || !library.TryCopy(rng, edges, corner, windowSize, used, exhausted, out var copied, keepClear))
                {
                    continue;
                }

                var kept = KeepSupported(copied.Where(piece => held.Contains(piece.Id)).ToArray());
                var pieces = kept.Select(piece => (piece.Id, Point: lattice.ToWorld(piece.U, piece.V), piece.Z)).ToArray();
                if (!occupancy.FitsAll(pieces.Select(piece => (piece.Id, piece.Point.X, piece.Point.Y, piece.Z)).ToArray()))
                {
                    continue;
                }

                foreach (var piece in pieces)
                {
                    occupancy.Add(piece.Id, piece.Point.X, piece.Point.Y, piece.Z);
                }

                placed.AddRange(kept);

                for (int u = corner.U; u < corner.U + windowSize; u++)
                {
                    for (int v = corner.V; v < corner.V + windowSize; v++)
                    {
                        covered.Add(new Cell(u, v));
                    }
                }

                break;
            }
        }

        return placed.ToArray();
    }

    /// <summary>
    /// Returns the objects whose support is still present, repeated until nothing changes.
    /// </summary>
    public static PlacedDecor[] KeepSupported(IReadOnlyList<PlacedDecor> placed)
    {
        while (true)
        {
            var present = placed.Select(o => o.Source).ToHashSet();
            var kept = placed.Where(o => o.Support is null || present.Contains(o.Support.Value)).ToArray();
            if (kept.Length == placed.Count)
            {
                return kept;
            }

            placed = kept;
        }
    }

    /// <summary>
    /// Returns the cells a solid object's collision box, grown by a margin, touches, sampled at its corners, edge midpoints and center.
    /// </summary>
    public static HashSet<Cell> GetFootprint(Lattice lattice, ObjectTable objects, KnownObject id, float x, float y, float grow = CollisionBox.PlayerRadius)
    {
        return objects.TryFind(id, out var obj) && obj.IsSolid ? CollisionBox.Create(obj, x, y, CollisionBox.ExtentShare, grow).GetSampledCells(lattice) : [];
    }
}

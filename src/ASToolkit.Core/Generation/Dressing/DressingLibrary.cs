using System.Collections.Concurrent;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Represents the dressing of the original levels of one theme, indexed by the walls and doors around square windows of cells.
/// </summary>
public sealed class DressingLibrary
{
    private const float FloorHeight = 5;
    private const int StripeFrameWidth = 16;
    private const int FirstStripeFrame = 6;
    private const int LastStripeFrame = 9;

    private readonly List<LevelModel> _sources;
    private readonly ConcurrentDictionary<(int Source, int Index), Cell[]> _footprints = new();
    private readonly List<Dictionary<Cell, List<int>>> _objectsByCell;
    private readonly Dictionary<(int Size, string Key), List<(int Source, Cell Corner)>> _windows;
    private readonly WallClearanceTable _clearance;
    private readonly FacingTable _facings;
    private readonly ObjectTable _objects;

    private DressingLibrary(List<LevelModel> sources, List<Dictionary<Cell, List<int>>> objectsByCell, Dictionary<(int, string), List<(int, Cell)>> windows, int[] sizes,
        WallClearanceTable clearance, FacingTable facings, ObjectTable objects)
    {
        _facings = facings;
        _objects = objects;
        _sources = sources;
        _objectsByCell = objectsByCell;
        _windows = windows;
        _clearance = clearance;
        WindowSizes = sizes;
    }

    /// <summary>
    /// Gets the window sizes, largest first.
    /// </summary>
    public int[] WindowSizes { get; }

    /// <summary>
    /// Builds the library from the original levels of a theme, keeping only the allowed decor.
    /// </summary>
    public static DressingLibrary Build(ReferenceLevelSet references, ObjectTable objects, LevelTheme theme, IEnumerable<string> levels, Func<KnownObject, float, bool> isAllowed,
        WallClearanceTable clearance, FacingTable facings)
    {
        int[] sizes = theme == LevelTheme.Lab ? [3, 2, 1] : [4, 2];
        var sources = new List<LevelModel>();
        var companionTests = new List<Func<KnownObject, float, float, bool>>();
        foreach (string level in levels)
        {
            var model = references.GetLevel(level).Model;
            if (model.Theme == theme && model.Edges.Count >= 100)
            {
                sources.Add(model);
                companionTests.Add(references.Relations.GetCompanionTest(theme, references.GetLevel(level)));
            }
        }

        var byCell = new List<Dictionary<Cell, List<int>>>();
        var windows = new Dictionary<(int, string), List<(int, Cell)>>();
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var m = sources[sourceIndex];
            var cellObjects = new Dictionary<Cell, List<int>>();
            for (int levelObjectIndex = 0; levelObjectIndex < m.Objects.Count; levelObjectIndex++)
            {
                var levelObject = m.Objects[levelObjectIndex];
                var point = m.Lattice.ToWorld(levelObject.U, levelObject.V);
                if (!isAllowed(levelObject.Id, levelObject.Z) || companionTests[sourceIndex](levelObject.Id, point.X, point.Y))
                {
                    continue;
                }

                var c = new Cell((int)MathF.Floor(m.Objects[levelObjectIndex].U), (int)MathF.Floor(m.Objects[levelObjectIndex].V));
                if (!cellObjects.TryGetValue(c, out var list))
                {
                    list = [];
                    cellObjects[c] = list;
                }

                list.Add(levelObjectIndex);
            }

            byCell.Add(cellObjects);
            foreach (var corner in m.Cells.OrderBy(c => c.U).ThenBy(c => c.V))
            {
                foreach (int size in sizes)
                {
                    if (!IsWindowInside(m.Cells, corner, size))
                    {
                        continue;
                    }

                    var key = (size, GetWallKey(m.Edges, corner, size));
                    if (!windows.TryGetValue(key, out var list))
                    {
                        list = [];
                        windows[key] = list;
                    }

                    list.Add((sourceIndex, corner));
                }
            }
        }

        return new DressingLibrary(sources, byCell, windows, sizes, clearance, facings, objects);
    }

    /// <summary>
    /// Returns the walls and doors around a window as a key; walls of any kind count alike, and so do doors.
    /// </summary>
    public static string GetWallKey(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell corner, int size)
    {
        Span<char> key = stackalloc char[2 * size * (size + 1)];
        int i = 0;
        for (int u = corner.U; u < corner.U + size; u++)
        {
            for (int index = corner.V; index <= corner.V + size; index++)
            {
                key[i++] = Classify(edges, new EdgeKey('v', u, index));
            }
        }

        for (int index = corner.U; index <= corner.U + size; index++)
        {
            for (int v = corner.V; v < corner.V + size; v++)
            {
                key[i++] = Classify(edges, new EdgeKey('u', index, v));
            }
        }

        return new string(key);
    }

    /// <summary>
    /// Copies the objects of a random source window with the same walls, and none of its cells used before, into the target
    /// window, and returns whether any source window matched. An object that would stand closer to a wall than in its source and than
    /// the originals usually allow, that would face the walls in a way the originals rarely or never do, or that would stand away from walls
    /// where the originals nearly always stand it by one, is left out. Windows whose solid pieces stay off the cells to keep clear come first.
    /// </summary>
    public bool TryCopy(Random rng, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell corner, int size, ISet<(int Source, Cell Cell)> used, out List<PlacedDecor> placed,
        IReadOnlySet<Cell> keepClear)
    {
        placed = [];
        if (!_windows.TryGetValue((size, GetWallKey(edges, corner, size)), out var all))
        {
            return false;
        }

        (int Source, Cell Corner)? pick = null;
        int best = int.MaxValue;
        int start = rng.Next(all.Count);
        for (int step = 0; step < all.Count && best > 0; step++)
        {
            var match = all[(start + step) % all.Count];
            if (GetCells(match.Corner, size).Any(cell => used.Contains((match.Source, cell))))
            {
                continue;
            }

            int tier = BlocksCells(match.Source, match.Corner, corner, size, keepClear) ? 1 : 0;
            if (tier < best)
            {
                pick = match;
                best = tier;
            }
        }

        if (pick is null)
        {
            return false;
        }

        (int si, var source) = pick.Value;
        foreach (var cell in GetCells(source, size))
        {
            used.Add((si, cell));
        }

        for (int du = 0; du < size; du++)
        {
            for (int dv = 0; dv < size; dv++)
            {
                if (!_objectsByCell[si].TryGetValue(new Cell(source.U + du, source.V + dv), out var indexes))
                {
                    continue;
                }

                foreach (int index in indexes)
                {
                    PlaceObject(si, index, source, corner, edges, placed);
                }
            }
        }

        return true;
    }

    private bool PlaceObject(int si, int index, Cell source, Cell corner, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, List<PlacedDecor> result)
    {
        var m = _sources[si];
        var o = m.Objects[index];
        (float u, float v) = (o.U - source.U + corner.U, o.V - source.V + corner.V);
        if (IsStripe(o) || IsHung(o) && !IsHungAsInSource(o, m.Edges, edges, u, v) || IsTooCloseToWall(o, m.Edges, edges, u, v) || !HasSameWalls(o, m, edges, source, corner) || (o.Z < FloorHeight || o.Id.IsWallMounted) && !(_facings.Fits(o.Id, o.Dir, edges, u, v) && _facings.IsCommon(o.Id, o.Dir, edges, u, v) && _facings.IsByWall(o.Id, edges, u, v))
            || o.Id.IsWallMounted && !_facings.Hangs(o.Id, o.Dir, o.Z, edges, u, v))
        {
            return false;
        }

        byte[] payload = o.Id == KnownObject.Crate ? KnownObject.CreateEmptyCratePayload() : o.Payload;
        (int, int)? support = o.SupportIndex is >= 0 ? (si, o.SupportIndex.Value) : null;
        result.Add(new PlacedDecor(o.Id, u, v, o.Z, o.Dir, payload, (si, index), support, SpriteSource.CopiedWindow));
        return true;
    }

    // Return whether a solid piece of a source window would cover one of the cells once copied to the target corner
    private bool BlocksCells(int si, Cell sourceCorner, Cell corner, int size, IReadOnlySet<Cell> cells)
    {
        foreach (var cell in GetCells(sourceCorner, size))
        {
            if (_objectsByCell[si].TryGetValue(cell, out var indexes) && indexes.Any(index => BlocksFootprint(si, index, sourceCorner, corner, cells)))
            {
                return true;
            }
        }

        return false;
    }

    private bool BlocksFootprint(int si, int index, Cell sourceCorner, Cell corner, IReadOnlySet<Cell> cells)
    {
        var footprint = _footprints.GetOrAdd((si, index), key =>
        {
            var o = _sources[key.Source].Objects[key.Index];
            (float x, float y) = _sources[key.Source].Lattice.ToWorld(o.U, o.V);
            return Dresser.GetFootprint(_sources[key.Source].Lattice, _objects, o.Id, x, y).ToArray();
        });
        return footprint.Any(c => cells.Contains(new Cell(c.U - sourceCorner.U + corner.U, c.V - sourceCorner.V + corner.V)));
    }

    // Leave floor stripes (131 frames 6 to 9) to the door, missile and cannon scenes
    private static bool IsStripe(DecorObject o) => o is { Id: KnownObject.WallInscriptions, Z: < FloorHeight } && o.Dir / StripeFrameWidth is >= FirstStripeFrame and <= LastStripeFrame;

    private static bool IsHung(DecorObject o) => o.Id is KnownObject.WallObjects or KnownObject.WallInscriptions && o.Z > FloorHeight;

    // Keep a hung object on the same side of a wall and as far from it as in its source
    private static bool IsHungAsInSource(DecorObject o, IReadOnlyDictionary<EdgeKey, EdgeKind> sourceEdges, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v)
    {
        var source = sourceEdges.FindNearestWall(o.U, o.V);
        var target = edges.FindNearestWall(u, v);
        return source is not null && target is not null && source.Value.Side == target.Value.Side && MathF.Abs(source.Value.Distance - target.Value.Distance) <= EdgeMapExtensions.WallSlack;
    }

    /// <summary>
    /// Returns whether a window of cells lies inside a set of cells.
    /// </summary>
    public static bool IsWindowInside(IReadOnlySet<Cell> cells, Cell corner, int size)
    {
        for (int u = corner.U; u < corner.U + size; u++)
        {
            for (int v = corner.V; v < corner.V + size; v++)
            {
                if (!cells.Contains(new Cell(u, v)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<Cell> GetCells(Cell corner, int size)
    {
        for (int u = corner.U; u < corner.U + size; u++)
        {
            for (int v = corner.V; v < corner.V + size; v++)
            {
                yield return new Cell(u, v);
            }
        }
    }

    private bool IsTooCloseToWall(DecorObject o, IReadOnlyDictionary<EdgeKey, EdgeKind> sourceEdges, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, float u, float v)
    {
        if (!_clearance.TryGetClearance(o.Id, out _, out float low))
        {
            return false;
        }

        float target = edges.MeasureClearance(u, v);
        return target < low && target < sourceEdges.MeasureClearance(o.U, o.V) - EdgeMapExtensions.WallSlack;
    }

    // Keep a solid piece only where no wall it does not cross in its source runs through its footprint
    private bool HasSameWalls(DecorObject o, LevelModel source, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, Cell sourceCorner, Cell corner)
    {
        (float x, float y) = source.Lattice.ToWorld(o.U, o.V);
        var footprint = Dresser.GetFootprint(source.Lattice, _objects, o.Id, x, y);
        foreach (var cell in footprint)
        {
            var sourceNeighbors = EdgeKey.GetNeighbors(cell);
            var targetNeighbors = EdgeKey.GetNeighbors(new Cell(cell.U - sourceCorner.U + corner.U, cell.V - sourceCorner.V + corner.V));
            for (int index = 0; index < 4; index++)
            {
                if (footprint.Contains(sourceNeighbors[index].Cell) && !IsWall(source.Edges, sourceNeighbors[index].Edge) && IsWall(edges, targetNeighbors[index].Edge))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsWall(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, EdgeKey k) => edges.TryGetValue(k, out EdgeKind kind) && kind is EdgeKind.Wall or EdgeKind.Door or EdgeKind.SecretWall;

    private static char Classify(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, EdgeKey k) =>
        edges.TryGetValue(k, out EdgeKind kind) ? kind is EdgeKind.Door or EdgeKind.DoubleDoor or EdgeKind.DoubleDoorEnd ? '2' : '1' : '0';
}

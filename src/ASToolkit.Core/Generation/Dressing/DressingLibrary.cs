using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Represents the dressing of the original levels of one theme, indexed by the walls and doors around square windows of cells.
/// </summary>
public sealed class DressingLibrary
{
    private const double CopyTolerance = 0.05;
    private const double FloorHeight = 5;

    private readonly List<LevelModel> _sources;
    private readonly List<Dictionary<Cell, List<int>>> _objectsByCell;
    private readonly Dictionary<(int Size, string Key), List<(int Source, Cell Corner)>> _windows;
    private readonly WallClearanceTable _clearance;
    private readonly FacingTable _facings;

    private DressingLibrary(List<LevelModel> sources, List<Dictionary<Cell, List<int>>> objectsByCell, Dictionary<(int, string), List<(int, Cell)>> windows, int[] sizes,
        WallClearanceTable clearance, FacingTable facings)
    {
        _facings = facings;
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
    public static DressingLibrary Build(string gameFolder, ObjectTable objects, LevelTheme theme, IEnumerable<string> levels, Func<KnownObject, double, bool> isAllowed,
        WallClearanceTable clearance, FacingTable facings)
    {
        int[] sizes = theme == LevelTheme.Lab ? [3, 2, 1] : [4, 2];
        var sources = new List<LevelModel>();
        foreach (string level in levels)
        {
            var model = LevelExtractor.Extract(level, GameMap.Load(Path.Combine(gameFolder, "Maps", level)), objects);
            if (model is not null && model.Theme == theme && model.Edges.Count >= 100)
            {
                sources.Add(model);
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
                if (!isAllowed(m.Objects[levelObjectIndex].Id, m.Objects[levelObjectIndex].Z))
                {
                    continue;
                }

                var c = new Cell((int)Math.Floor(m.Objects[levelObjectIndex].U), (int)Math.Floor(m.Objects[levelObjectIndex].V));
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

        return new DressingLibrary(sources, byCell, windows, sizes, clearance, facings);
    }

    /// <summary>
    /// Returns the walls and doors around a window as a key; walls of any kind count alike, and so do doors.
    /// </summary>
    public static string GetWallKey(IReadOnlyDictionary<EdgeKey, char> edges, Cell corner, int size)
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
    /// Copies the objects of a random source window with the same walls into the target window, and returns whether any
    /// source window matched. An object that would stand closer to a wall than in its source and than the originals usually
    /// allow, or that would face the walls in a way no original does, is left out.
    /// </summary>
    public bool TryCopy(Random rng, IReadOnlyDictionary<EdgeKey, char> edges, Cell corner, int size, out List<PlacedDecor> placed)
    {
        placed = [];
        if (!_windows.TryGetValue((size, GetWallKey(edges, corner, size)), out var matches))
        {
            return false;
        }

        (int si, var source) = matches[rng.Next(matches.Count)];
        var m = _sources[si];
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
                    var o = m.Objects[index];
                    (double u, double v) = (o.U - source.U + corner.U, o.V - source.V + corner.V);
                    if (IsTooCloseToWall(o, m.Edges, edges, u, v) || o.Z < FloorHeight && !_facings.Fits(o.Id, o.Dir, edges, u, v))
                    {
                        continue;
                    }

                    byte[] payload = o.Id == KnownObject.Crate ? new byte[12] : o.Payload;
                    (int, int)? support = o.SupportIndex is >= 0 ? (si, o.SupportIndex.Value) : null;
                    placed.Add(new PlacedDecor(o.Id, u, v, o.Z, o.Dir, payload, (si, index), support));
                }
            }
        }

        return true;
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

    private bool IsTooCloseToWall(DecorObject o, IReadOnlyDictionary<EdgeKey, char> sourceEdges, IReadOnlyDictionary<EdgeKey, char> edges, double u, double v)
    {
        if (!_clearance.TryGetClearance(o.Id, out _, out double low))
        {
            return false;
        }

        double target = WallClearanceTable.MeasureDistance(edges, u, v);
        return target < low && target < WallClearanceTable.MeasureDistance(sourceEdges, o.U, o.V) - CopyTolerance;
    }

    private static char Classify(IReadOnlyDictionary<EdgeKey, char> edges, EdgeKey k) =>
        edges.TryGetValue(k, out char kind) ? kind is 'D' or 'E' or 'e' ? '2' : '1' : '0';
}

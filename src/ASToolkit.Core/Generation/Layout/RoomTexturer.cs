using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Layout;

// Cut room outlines on a built layout, the planned path-room cuts first, each cut only where every door keeps its width and the room stays whole
internal sealed class RoomTexturer
{
    private const float MaxCutShare = 0.45f;

    private static readonly float[] CutScales = [1f, 0.6f, 0.4f];

    private readonly int _doorMin;
    private readonly int _minCut;
    private readonly AreaThemeModel _model;
    private readonly Random _rng;
    private readonly AreaBuildCounts _counts;
    private readonly bool _wallStepUnits;
    private readonly Dictionary<int, HashSet<Cell>> _cells = [];
    private readonly Dictionary<Cell, int> _owner = [];
    private readonly Dictionary<int, HashSet<int>> _joins = [];

    public RoomTexturer(AreaThemeModel model, LevelTheme theme, Random rng, AreaBuildCounts counts)
    {
        _wallStepUnits = theme == LevelTheme.Lab;
        _doorMin = (theme == LevelTheme.Lab ? ThemeSettings.Lab : ThemeSettings.Military).MinShared;
        _minCut = theme == LevelTheme.Lab ? 2 : 1;
        _model = model;
        _rng = rng;
        _counts = counts;
    }

    public void Apply(BlockResult result)
    {
        foreach ((int room, var cells) in result.Cells)
        {
            Own(room, cells);
        }

        for (int index = 0; index < result.Corridors.Count; index++)
        {
            Own(GetCorridorId(index), result.Corridors[index].Cells);
            Join(GetCorridorId(index), result.Corridors[index].A);
            Join(GetCorridorId(index), result.Corridors[index].B);
        }

        foreach ((int from, int to, _) in result.Realized)
        {
            if (_cells.ContainsKey(from) && _cells.ContainsKey(to) && CountContacts(_cells[from], to) >= _doorMin)
            {
                Join(from, to);
            }
        }

        foreach ((int room, var cuts) in result.Cuts)
        {
            _counts.CutsDrawn++;
            if (CutPlanned(room, cuts))
            {
                _counts.CutsBuilt++;
            }
        }

        foreach (int room in result.Cells.Keys.Order())
        {
            if (result.Halls.Contains(room) || result.Decided.Contains(room) || result.Connectors.Contains(room))
            {
                continue;
            }

            var cells = _cells[room];
            int shortSide = Math.Min(cells.Max(cell => cell.U) - cells.Min(cell => cell.U) + 1, cells.Max(cell => cell.V) - cells.Min(cell => cell.V) + 1);
            var fit = _model.CutShares[AreaThemeModel.GetSizeClass(shortSide)];
            if (_rng.NextSingle() >= fit.Share)
            {
                continue;
            }

            var names = fit.Classes.Where(pair => _model.CutSizes.ContainsKey(pair.Key)).ToArray();
            if (names.Length == 0)
            {
                continue;
            }

            var name = DrawWeighted(names);
            _counts.CutsDrawn++;
            double share = Math.Min(MaxCutShare, _model.CutSizes[name].Draw(_rng) / 100);
            if (CutScales.Any(scale => Carve(room, name, share * scale)))
            {
                _counts.CutsBuilt++;
            }
        }

        foreach (int room in result.Cells.Keys.ToArray())
        {
            result.Cells[room] = _cells[room];
        }
    }

    public static int GetCorridorId(int index) => -(index + 1);

    private void Own(int id, HashSet<Cell> cells)
    {
        _cells[id] = cells.ToHashSet();
        foreach (var cell in cells)
        {
            _owner[cell] = id;
        }
    }

    private void Join(int a, int b)
    {
        GetJoins(a).Add(b);
        GetJoins(b).Add(a);
    }

    private HashSet<int> GetJoins(int id)
    {
        if (!_joins.TryGetValue(id, out var set))
        {
            set = [];
            _joins[id] = set;
        }

        return set;
    }

    private int CountContacts(IEnumerable<Cell> cells, int other)
    {
        int count = 0;
        foreach (var cell in cells)
        {
            foreach (var next in new[] { cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 } })
            {
                if (_owner.TryGetValue(next, out int found) && found == other)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private bool Keeps(int room, HashSet<Cell> rest) =>
        rest.Count > 0 && IsConnected(rest) && !(_wallStepUnits && HasStep(rest)) && GetJoins(room).All(other => CountContacts(rest, other) >= _doorMin);

    // Find a straight run of the outline one unit long
    private static bool HasStep(HashSet<Cell> cells)
    {
        var runs = new Dictionary<(bool Vertical, int Line, bool Inside), List<int>>();
        foreach (var cell in cells)
        {
            foreach ((int du, int dv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (cells.Contains(new Cell(cell.U + du, cell.V + dv)))
                {
                    continue;
                }

                var key = du != 0 ? (true, cell.U + Math.Max(0, du), du > 0) : (false, cell.V + Math.Max(0, dv), dv > 0);
                if (!runs.TryGetValue(key, out var list))
                {
                    list = [];
                    runs[key] = list;
                }

                list.Add(du != 0 ? cell.V : cell.U);
            }
        }

        foreach (var list in runs.Values)
        {
            list.Sort();
            int length = 1;
            for (int index = 1; index <= list.Count; index++)
            {
                if (index < list.Count && list[index] == list[index - 1] + 1)
                {
                    length++;
                    continue;
                }

                if (length == 1)
                {
                    return true;
                }

                length = 1;
            }
        }

        return false;
    }

    private static bool IsConnected(HashSet<Cell> cells)
    {
        var seen = new HashSet<Cell> { cells.First() };
        var queue = new Queue<Cell>(seen);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var next in new[] { cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 } })
            {
                if (cells.Contains(next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return seen.Count == cells.Count;
    }

    private void Commit(int room, HashSet<Cell> rest)
    {
        foreach (var cell in _cells[room].Where(cell => !rest.Contains(cell)))
        {
            _owner.Remove(cell);
        }

        _cells[room] = rest;
    }

    private bool CutPlanned(int room, List<(int Corner, int W, int H)> cuts)
    {
        var cells = _cells[room];
        int u = cells.Min(cell => cell.U), v = cells.Min(cell => cell.V);
        int w = cells.Max(cell => cell.U) - u + 1, h = cells.Max(cell => cell.V) - v + 1;
        var removed = new HashSet<Cell>();
        foreach ((int corner, int cw, int ch) in cuts)
        {
            int cu = corner is 0 or 3 ? u : u + w - cw;
            int cv = corner is 0 or 1 ? v : v + h - ch;
            AddRect(removed, cu, cv, cw, ch);
        }

        var rest = cells.Where(cell => !removed.Contains(cell)).ToHashSet();
        if (!Keeps(room, rest))
        {
            return false;
        }

        Commit(room, rest);
        return true;
    }

    // Cut the room's corners for the drawn outline class, keeping every door and the room whole
    private bool Carve(int room, OutlineClass name, double share)
    {
        var cells = _cells[room];
        int u = cells.Min(cell => cell.U), v = cells.Min(cell => cell.V);
        int w = cells.Max(cell => cell.U) - u + 1, h = cells.Max(cell => cell.V) - v + 1;
        int corners = BlockLayout.GetCornerCount(name);
        double each = share / corners;
        int[] order = Enumerable.Range(0, 4).ToArray();
        _rng.Shuffle(order);
        foreach (int first in order)
        {
            int[] picks = corners switch
            {
                1 => [first],
                2 => [first, (first + 1) % 4],
                _ => [0, 1, 2, 3]
            };

            bool pairedU = corners == 4 || (corners == 2 && first is 0 or 2);
            bool pairedV = corners == 4 || (corners == 2 && first is 1 or 3);
            int spareW = w - _doorMin - (pairedU ? _minCut : 0);
            int spareH = h - _doorMin - (pairedV ? _minCut : 0);
            if (spareW < _minCut || spareH < _minCut)
            {
                continue;
            }

            var removed = new HashSet<Cell>();
            foreach (int corner in picks)
            {
                int cw = Math.Max(_minCut, Math.Min(spareW, (int)Math.Round(w * Math.Sqrt(each) * (0.6 + 0.8 * _rng.NextDouble()))));
                int ch = Math.Max(_minCut, Math.Min(spareH, (int)Math.Round(each * w * h / cw)));
                AddRect(removed, corner is 0 or 3 ? u : u + w - cw, corner is 0 or 1 ? v : v + h - ch, cw, ch);
            }

            var rest = cells.Where(cell => !removed.Contains(cell)).ToHashSet();
            if (Keeps(room, rest))
            {
                Commit(room, rest);
                return true;
            }

            if (corners == 4)
            {
                break;
            }
        }

        return false;
    }

    private static void AddRect(HashSet<Cell> set, int u, int v, int w, int h)
    {
        for (int a = u; a < u + w; a++)
        {
            for (int b = v; b < v + h; b++)
            {
                set.Add(new Cell(a, b));
            }
        }
    }

    private OutlineClass DrawWeighted(KeyValuePair<OutlineClass, float>[] weights)
    {
        float draw = _rng.NextSingle() * weights.Sum(pair => pair.Value);
        foreach (var pair in weights)
        {
            draw -= pair.Value;
            if (draw < 0)
            {
                return pair.Key;
            }
        }

        return weights[^1].Key;
    }
}

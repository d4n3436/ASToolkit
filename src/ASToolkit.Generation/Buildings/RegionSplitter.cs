using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Buildings;

internal sealed class RegionSplitter
{
    private readonly GrammarRandom _random;
    private readonly int _minArea;
    private readonly int _maxArea;
    private readonly int _thickness;
    private readonly float _stepShare;
    private readonly float _secondStepShare;

    public RegionSplitter(GrammarRandom random, int minArea, int maxArea, int thickness, float stepShare, float secondStepShare)
    {
        _random = random;
        _minArea = minArea;
        _maxArea = maxArea;
        _thickness = thickness;
        _stepShare = stepShare;
        _secondStepShare = secondStepShare;
    }

    private List<HashSet<Cell>> SplitRegion(HashSet<Cell> region, bool strip)
    {
        var box = region.GetBounds();
        bool vertical = (box.W >= box.H) != (_random.Next() < 0.2f);
        var cells = region.Select(Transpose).ToHashSet();
        var turned = cells.GetBounds();
        (int a0, int b0, int a1, int b1) = (turned.U, turned.V, turned.U + turned.W - 1, turned.V + turned.H - 1);
        if (a1 - a0 < 14)
        {
            return [];
        }

        int a = _random.Inclusive(a0 + 7, a1 - 6);
        var steps = new List<(int At, int Shift)>();

        if (b1 - b0 >= 12 && _random.Next() < _stepShare)
        {
            int first = DrawStepShift(a, 7);
            if (first != 0)
            {
                steps.Add((_random.Inclusive(b0 + 5, b1 - 5), first));
            }

            if (steps.Count == 1 && b1 - b0 >= 22 && _random.Next() < _secondStepShare)
            {
                int second = _random.Inclusive(b0 + 5, b1 - 5);
                int shift = DrawStepShift(a + first, 6);
                if (Math.Abs(second - steps[0].At) >= 6 && shift != 0)
                {
                    steps.Add((second, shift));
                }
            }
        }

        int width = strip ? _random.Inclusive(5, 6) : 0;
        int span = strip ? Math.Min(b1 - b0 + 1, _random.Inclusive(8, 12)) : 0;
        int from = _random.Next() < 0.5f ? b0 : b1 - span + 1;
        var low = new HashSet<Cell>();
        var middle = new HashSet<Cell>();
        var high = new HashSet<Cell>();
        foreach (var cell in cells)
        {
            int cut = a;
            foreach ((int at, int shift) in steps)
            {
                cut += cell.V >= at ? shift : 0;
            }

            (cell.U < cut ? low : cell.U < cut + width && cell.V >= from && cell.V < from + span ? middle : high).Add(cell);
        }

        var parts = strip ? new List<HashSet<Cell>> { low, high, middle } : [low, high];
        if (Math.Min(low.Count, high.Count) < _minArea || parts.Any(part => !part.IsThick(_thickness) || !part.IsConnected()))
        {
            return [];
        }

        return parts.Select(part => part.Select(Transpose).ToHashSet()).ToList();

        // Keep steps clear of the region's sides
        int DrawStepShift(int at, int most)
        {
            int toward = _random.Next() < 0.5f ? -1 : 1;
            int room = toward < 0 ? at - (a0 + _thickness) : a1 - _thickness - (strip ? 6 : 0) - at;
            return room >= 4 ? toward * _random.Inclusive(4, Math.Min(most, room)) : 0;
        }

        Cell Transpose(Cell cell) => vertical ? cell : new Cell(cell.V, cell.U);
    }

    public RegionPartition Partition(HashSet<Cell> envelope, int count, bool hasPassage)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var regions = new List<HashSet<Cell>> { envelope };
            HashSet<Cell> strip = [];
            while (regions.Count < count && TrySplitLargest(regions, hasPassage && strip.Count == 0 && regions.Count == count - 1, out var source, out var parts))
            {
                regions.Remove(source);
                regions.Add(parts[0]);
                regions.Add(parts[1]);
                if (parts.Count > 2)
                {
                    strip = parts[2];
                }
            }

            if (regions.Count == count && regions.All(region => region.Count <= _maxArea))
            {
                return new RegionPartition(regions, strip);
            }
        }

        return new RegionPartition([], []);
    }

    private bool TrySplitLargest(List<HashSet<Cell>> regions, bool withStrip, out HashSet<Cell> source, out List<HashSet<Cell>> parts)
    {
        foreach (var region in withStrip ? regions.OrderByDescending(region => region.Count).ToList() : [regions.MaxBy(region => region.Count)!])
        {
            parts = [];
            for (int tries = 0; tries < 30 && parts.Count == 0; tries++)
            {
                parts = SplitRegion(region, withStrip);
            }

            if (parts.Count > 0)
            {
                source = region;
                return true;
            }
        }

        source = [];
        parts = [];
        return false;
    }

    public static List<HashSet<int>> GetAdjacency(List<HashSet<Cell>> regions, int minWall) => regions
        .Select((region, i) => Enumerable.Range(0, regions.Count).Where(j => j != i && region.GetLongestWall(regions[j]).Length >= minWall).ToHashSet())
        .ToList();

    public List<List<int>> FindPaths(List<HashSet<int>> adjacency, int length, Func<int, bool> isStart, Func<int, bool> isEnd, int inner)
    {
        var found = new List<List<int>>();
        var stack = new Stack<List<int>>();
        foreach (int start in _random.Shuffled(Enumerable.Range(0, adjacency.Count).Where(isStart)))
        {
            stack.Push([start]);
        }

        while (stack.Count > 0 && found.Count < 400)
        {
            var path = stack.Pop();
            if (path.Count == length)
            {
                if (isEnd(path[^1]) && (inner < 0 || (path.IndexOf(inner) > 0 && path[^1] != inner)))
                {
                    found.Add(path);
                }

                continue;
            }

            foreach (int next in _random.Shuffled(adjacency[path[^1]].Where(next => !path.Contains(next))))
            {
                stack.Push(path.Append(next).ToList());
            }
        }

        return found;
    }
}

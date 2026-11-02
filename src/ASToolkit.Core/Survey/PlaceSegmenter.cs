using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the cutting of a level's floor into places.
/// </summary>
internal sealed class PlaceSegmenter
{
    private const float NeckRatio = 0.6f;

    private const int RimCells = 2;

    private const int MaxFlankCells = 2;

    private const int MaxSnapCells = 4;

    private const float MaxPassageShare = 0.5f;

    private const int MaxPocketCells = 64;

    private const int MaxHoleCells = 2;

    private const int ContentSpacingCells = 300;

    private const int MaxStripCells = 5;

    private const int MinStripRatio = 4;

    private const int MinStripCells = 24;

    private const int OutlineCells = 3;

    private const int MaxStepAlignments = 64;

    private readonly int _maxOpeningCells;
    private readonly int _maxStubCells;
    private readonly int _minPlaceCells;
    private readonly int _maxClosetCells;
    private readonly int _minRoomCells;
    private readonly int _minCorridorCells;
    private readonly int _pocketCells;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaceSegmenter"/> class.
    /// </summary>
    /// <param name="maxOpeningCells">The widest wall gap that parts places, in cells.</param>
    /// <param name="maxStubCells">The longest carry of a wall stub's line, in cells.</param>
    /// <param name="minPlaceCells">The smallest place kept, in cells.</param>
    /// <param name="maxClosetCells">The largest closet, in cells.</param>
    /// <param name="minRoomCells">The narrowest room of a neck cut, in cells.</param>
    /// <param name="minCorridorCells">The shortest corridor kept, in cells.</param>
    /// <param name="pocketCells">The largest pocket merged on open plans, in cells.</param>
    public PlaceSegmenter(int maxOpeningCells, int maxStubCells, int minPlaceCells, int maxClosetCells, int minRoomCells, int minCorridorCells, int pocketCells)
    {
        _maxOpeningCells = maxOpeningCells;
        _maxStubCells = maxStubCells;
        _minPlaceCells = minPlaceCells;
        _maxClosetCells = maxClosetCells;
        _minRoomCells = minRoomCells;
        _minCorridorCells = minCorridorCells;
        _pocketCells = pocketCells;
    }

    /// <summary>
    /// Cuts the floor into places.
    /// </summary>
    /// <param name="grid">The routing grid.</param>
    /// <param name="tour">The cells the tour walks.</param>
    /// <param name="roofed">The floor cells hidden under the roof.</param>
    /// <param name="openPlan">Whether the level has no doors.</param>
    /// <param name="content">The cells with content or tour.</param>
    /// <param name="furnished">The cells with content other than doors, or tour.</param>
    public PlaceSegmentation Segment(RouteGrid grid, IReadOnlyCollection<Cell> tour, IReadOnlySet<Cell> roofed, bool openPlan, IReadOnlySet<Cell> content, IReadOnlySet<Cell> furnished)
    {
        var edges = grid.Edges;
        var shown = grid.Cells.Where(cell => !roofed.Contains(cell)).ToHashSet();
        shown.UnionWith(tour);
        var floor = shown.Flood(tour, IsClosed);
        for (int step = 0; step < RimCells; step++)
        {
            var rim = new List<Cell>();
            foreach (var cell in floor)
            {
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    if (grid.Cells.Contains(neighbor) && !floor.Contains(neighbor) && !edges.ContainsKey(edge))
                    {
                        rim.Add(neighbor);
                    }
                }
            }

            floor.UnionWith(rim);
        }

        var closets = new HashSet<Cell>();
        foreach (var cell in floor)
        {
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (grid.Cells.Contains(neighbor) && !floor.Contains(neighbor) && !closets.Contains(neighbor) && edges.TryGetValue(edge, out var kind) && kind == EdgeKind.SecretWall)
                {
                    var closet = grid.Cells.Flood([neighbor], IsClosed);
                    if (closet.Count <= _maxClosetCells)
                    {
                        closets.UnionWith(closet);
                    }
                }
            }
        }

        floor.UnionWith(closets);
        var hidden = grid.BlockedCells.Concat(shown.Where(cell => !floor.Contains(cell))).ToHashSet();
        var blocked = new HashSet<Cell>();
        foreach (var cell in floor)
        {
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (grid.BlockedCells.Contains(neighbor) && !blocked.Contains(neighbor) && !IsWall(edge))
                {
                    var pocket = hidden.Flood([neighbor], IsWall);
                    blocked.UnionWith(pocket.Count(pocketCell => !grid.BlockedCells.Contains(pocketCell)) <= MaxPocketCells ? pocket : grid.BlockedCells.Flood([neighbor], IsWall));
                }
            }
        }

        floor.UnionWith(blocked);
        var flanks = FindDoorFlanks(edges, floor);
        var barriers = new Dictionary<EdgeKey, EdgeKind>(edges);
        foreach (var flank in flanks)
        {
            barriers[flank] = EdgeKind.Wall;
        }

        var openings = new HashSet<EdgeKey>();
        if (openPlan)
        {
            openings.UnionWith(FindOpenings(barriers, floor, _maxOpeningCells));
            openings.UnionWith(FindStubLines(barriers, floor, _maxStubCells));
        }

        var placeOf = new Dictionary<Cell, int>();
        var places = new List<List<Cell>>();
        foreach (var cell in floor.OrderBy(cell => cell.U).ThenBy(cell => cell.V))
        {
            if (placeOf.ContainsKey(cell))
            {
                continue;
            }

            var region = floor.Flood([cell], edge => barriers.ContainsKey(edge) || openings.Contains(edge));
            foreach (var member in region)
            {
                placeOf[member] = places.Count;
            }

            places.Add(region.ToList());
        }

        if (openPlan)
        {
            DropEmpty(places, placeOf, floor, content, edges);
            SplitNecks(places, placeOf, barriers, openings, _minRoomCells, _minCorridorCells);
        }
        else
        {
            var gaps = FindOpenings(barriers, floor, _maxOpeningCells);
            SplitAtDoors(places, placeOf, barriers, gaps, openings);
            RemovePlaces(places, placeOf, floor, place => IsHollowStrip(place, furnished));
        }

        if (openPlan)
        {
            DropEmpty(places, placeOf, floor, content, edges);
        }

        MergeSmall(places, placeOf, barriers, openings, _minPlaceCells, openPlan ? _pocketCells : 0);
        AddHiddenFloor(places, placeOf, floor, grid, barriers);
        SmoothOutlines(places, placeOf, floor, barriers);
        return new PlaceSegmentation { Floor = floor, PlaceOf = placeOf, Places = places, Openings = openings, Flanks = flanks };

        bool IsClosed(EdgeKey edge) => edges.TryGetValue(edge, out var kind) && kind.IsClosed;

        bool IsWall(EdgeKey edge) => edges.TryGetValue(edge, out var kind) && kind.BlocksWalking;
    }

    // Give each small patch of floor no place holds to the place it opens onto most
    private static void AddHiddenFloor(List<List<Cell>> places, Dictionary<Cell, int> placeOf, HashSet<Cell> floor, RouteGrid grid, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var left = grid.Cells.Concat(grid.BlockedCells).Where(cell => !floor.Contains(cell)).ToHashSet();
        while (left.Count > 0)
        {
            var patch = left.Flood([left.First()], edges.ContainsKey);
            left.ExceptWith(patch);
            if (patch.Count(cell => !grid.BlockedCells.Contains(cell)) > MaxPocketCells)
            {
                continue;
            }

            var touches = new Dictionary<int, int>();
            foreach (var cell in patch)
            {
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    if (!edges.ContainsKey(edge) && placeOf.TryGetValue(neighbor, out int place))
                    {
                        touches[place] = touches.GetValueOrDefault(place) + 1;
                    }
                }
            }

            if (touches.Count == 0)
            {
                continue;
            }

            int owner = touches.MaxBy(touch => touch.Value).Key;
            foreach (var cell in patch)
            {
                placeOf[cell] = owner;
                places[owner].Add(cell);
                floor.Add(cell);
            }
        }
    }

    private static HashSet<EdgeKey> FindDoorFlanks(IReadOnlyDictionary<EdgeKey, EdgeKind> edges, HashSet<Cell> floor)
    {
        var flanks = new HashSet<EdgeKey>();
        var doors = edges.Where(pair => pair.Value is EdgeKind.Door or EdgeKind.Doorway or EdgeKind.ShutDoor).Select(pair => pair.Key).ToHashSet();
        foreach (var door in doors)
        {
            foreach (int step in new[] { -1, 1 })
            {
                if (doors.Contains(Shift(door, step)))
                {
                    continue;
                }

                var hole = new List<EdgeKey>();
                for (int offset = 1; offset <= MaxFlankCells + 1; offset++)
                {
                    var next = Shift(door, step * offset);
                    if (edges.ContainsKey(next))
                    {
                        if (hole.Count > 0)
                        {
                            flanks.UnionWith(hole);
                        }

                        break;
                    }

                    if (!floor.Contains(next.Cells.First) || !floor.Contains(next.Cells.Second))
                    {
                        break;
                    }

                    hole.Add(next);
                }
            }
        }

        return flanks;

        static EdgeKey Shift(EdgeKey edge, int offset) => edge.Axis == 'u' ? edge with { B = edge.B + offset } : edge with { A = edge.A + offset };
    }

    private static HashSet<EdgeKey> FindStubLines(Dictionary<EdgeKey, EdgeKind> edges, HashSet<Cell> floor, int maxStubCells)
    {
        var touching = new Dictionary<(int A, int B), int>();
        foreach (var edge in edges.Keys)
        {
            var (first, second) = GetEnds(edge);
            touching[first] = touching.GetValueOrDefault(first) + 1;
            touching[second] = touching.GetValueOrDefault(second) + 1;
        }

        var lines = new HashSet<EdgeKey>();
        foreach (var edge in edges.Keys)
        {
            var (first, second) = GetEnds(edge);
            foreach ((var end, var other, int step) in new[] { (first, second, -1), (second, first, 1) })
            {
                if (touching[end] != 1 || touching[other] < 2)
                {
                    continue;
                }

                var keys = new List<EdgeKey>();
                bool closed = false;
                for (int offset = 1; offset <= maxStubCells; offset++)
                {
                    var next = edge.Axis == 'u' ? edge with { B = edge.B + step * offset } : edge with { A = edge.A + step * offset };
                    if (edges.ContainsKey(next) || !floor.Contains(next.Cells.First) || !floor.Contains(next.Cells.Second))
                    {
                        closed = true;
                        break;
                    }

                    keys.Add(next);
                }

                if (closed && keys.Count > 0)
                {
                    lines.UnionWith(keys);
                }
            }
        }

        return lines;
    }

    private static ((int A, int B) First, (int A, int B) Second) GetEnds(EdgeKey edge) =>
        edge.Axis == 'u' ? ((edge.A, edge.B), (edge.A, edge.B + 1)) : ((edge.A, edge.B), (edge.A + 1, edge.B));

    private static HashSet<EdgeKey> FindOpenings(Dictionary<EdgeKey, EdgeKind> edges, HashSet<Cell> floor, int maxOpeningCells)
    {
        var openings = new HashSet<EdgeKey>();
        var lines = edges.Keys.GroupBy(k => (k.Axis, Line: k.Axis == 'u' ? k.A : k.B));
        foreach (var line in lines)
        {
            int[] positions = line.Select(k => k.Axis == 'u' ? k.B : k.A).Order().ToArray();
            for (int index = 1; index < positions.Length; index++)
            {
                int gap = positions[index] - positions[index - 1] - 1;
                if (gap < 1 || gap > maxOpeningCells)
                {
                    continue;
                }

                var keys = new List<EdgeKey>(gap);
                for (int p = positions[index - 1] + 1; p < positions[index]; p++)
                {
                    keys.Add(line.Key.Axis == 'u' ? new EdgeKey('u', line.Key.Line, p) : new EdgeKey('v', p, line.Key.Line));
                }

                if (keys.All(k => floor.Contains(k.Cells.First) && floor.Contains(k.Cells.Second)))
                {
                    openings.UnionWith(keys);
                }
            }
        }

        return openings;
    }

    private static void SplitAtDoors(List<List<Cell>> places, Dictionary<Cell, int> placeOf, Dictionary<EdgeKey, EdgeKind> edges, HashSet<EdgeKey> gaps,
        HashSet<EdgeKey> openings)
    {
        int count = places.Count;
        for (int index = 0; index < count; index++)
        {
            var region = places[index].ToHashSet();
            var doors = edges.Where(pair => pair.Value is EdgeKind.Door or EdgeKind.Doorway or EdgeKind.ShutDoor && region.Contains(pair.Key.Cells.First) && region.Contains(pair.Key.Cells.Second))
                .Select(pair => pair.Key).ToArray();
            if (doors.Length == 0)
            {
                continue;
            }

            var pieceOf = new Dictionary<Cell, int>();
            var pieces = new List<HashSet<Cell>>();
            foreach (var cell in region)
            {
                if (pieceOf.ContainsKey(cell))
                {
                    continue;
                }

                var piece = region.Flood([cell], edge => edges.ContainsKey(edge) || gaps.Contains(edge));
                foreach (var member in piece)
                {
                    pieceOf[member] = pieces.Count;
                }

                pieces.Add(piece);
            }

            int[] group = Enumerable.Range(0, pieces.Count).ToArray();
            var apart = doors.Select(door => (pieceOf[door.Cells.First], pieceOf[door.Cells.Second])).Where(pair => pair.Item1 != pair.Item2).ToArray();
            if (apart.Length == 0)
            {
                continue;
            }

            foreach (var gap in gaps.Where(gap => region.Contains(gap.Cells.First) && region.Contains(gap.Cells.Second)))
            {
                int first = Find(pieceOf[gap.Cells.First]), second = Find(pieceOf[gap.Cells.Second]);
                if (first != second && !apart.Any(pair => (Find(pair.Item1) == first && Find(pair.Item2) == second) || (Find(pair.Item1) == second && Find(pair.Item2) == first)))
                {
                    group[second] = first;
                }
            }

            int[] final = pieces.Select((_, pieceIndex) => Find(pieceIndex)).Distinct().ToArray();
            if (final.Length < 2)
            {
                continue;
            }

            for (int finalIndex = 0; finalIndex < final.Length; finalIndex++)
            {
                var cells = region.Where(cell => Find(pieceOf[cell]) == final[finalIndex]).ToList();
                int target = finalIndex == 0 ? index : places.Count;
                if (finalIndex == 0)
                {
                    places[index] = cells;
                }
                else
                {
                    places.Add(cells);
                }

                foreach (var cell in cells)
                {
                    placeOf[cell] = target;
                }
            }

            foreach (var gap in gaps.Where(gap => region.Contains(gap.Cells.First) && region.Contains(gap.Cells.Second)
                                                  && Find(pieceOf[gap.Cells.First]) != Find(pieceOf[gap.Cells.Second])))
            {
                openings.Add(gap);
            }

            int Find(int piece)
            {
                while (group[piece] != piece)
                {
                    piece = group[piece] = group[group[piece]];
                }

                return piece;
            }
        }
    }

    private static void SplitNecks(List<List<Cell>> places, Dictionary<Cell, int> placeOf, Dictionary<EdgeKey, EdgeKind> edges, HashSet<EdgeKey> openings,
        int minRoomCells, int minCorridorCells)
    {
        int count = places.Count;
        for (int index = 0; index < count; index++)
        {
            var pieces = new List<List<Cell>>();
            var queue = new Queue<List<Cell>>([places[index]]);
            while (queue.Count > 0)
            {
                var region = queue.Dequeue();
                var split = SplitNeck(region, edges, minRoomCells, minCorridorCells);
                if (split.Count < 2)
                {
                    pieces.Add(region);
                    continue;
                }

                foreach ((var piece, bool isRoom) in split)
                {
                    if (isRoom)
                    {
                        queue.Enqueue(piece);
                    }
                    else
                    {
                        pieces.Add(piece);
                    }
                }
            }

            pieces = MergeUnbounded(pieces, edges);
            if (pieces.Count < 2)
            {
                continue;
            }

            places[index] = pieces[0];
            places.AddRange(pieces.Skip(1));
            var pieceOf = new Dictionary<Cell, int>();
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                foreach (var cell in pieces[pieceIndex])
                {
                    pieceOf[cell] = pieceIndex;
                    placeOf[cell] = pieceIndex == 0 ? index : places.Count - pieces.Count + pieceIndex;
                }
            }

            foreach (var cell in pieceOf.Keys)
            {
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    if (pieceOf.TryGetValue(neighbor, out int other) && other != pieceOf[cell] && !edges.ContainsKey(edge))
                    {
                        openings.Add(edge);
                    }
                }
            }
        }
    }

    private static List<List<Cell>> MergeUnbounded(List<List<Cell>> pieces, IReadOnlyDictionary<EdgeKey, EdgeKind> edges)
    {
        var bars = new Dictionary<(int A, int B), (int Along, int Across)>();
        foreach (var edge in edges.Keys)
        {
            var (first, second) = GetEnds(edge);
            foreach (var vertex in new[] { first, second })
            {
                (int along, int across) = bars.GetValueOrDefault(vertex);
                bars[vertex] = edge.Axis == 'u' ? (along + 1, across) : (along, across + 1);
            }
        }

        var labelOf = new Dictionary<Cell, int>();
        for (int index = 0; index < pieces.Count; index++)
        {
            foreach (var cell in pieces[index])
            {
                labelOf[cell] = index;
            }
        }

        bool changed;
        do
        {
            changed = false;
            var cuts = new Dictionary<(int, int), List<EdgeKey>>();
            foreach ((var cell, int label) in labelOf)
            {
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    if (labelOf.TryGetValue(neighbor, out int other) && other > label && !edges.ContainsKey(edge))
                    {
                        if (!cuts.TryGetValue((label, other), out var cut))
                        {
                            cuts[(label, other)] = cut = [];
                        }

                        cut.Add(edge);
                    }
                }
            }

            foreach (((int label, int other), var cut) in cuts.OrderBy(pair => pair.Key))
            {
                if (IsBounded(cut) || (IsNarrow(cut, label, other) && IsPassage(cut)))
                {
                    continue;
                }

                if (!TrySnap(label, other) && !TrySnap(other, label))
                {
                    foreach (var cell in labelOf.Where(pair => pair.Value == other).Select(pair => pair.Key).ToArray())
                    {
                        labelOf[cell] = label;
                    }
                }

                changed = true;
                break;
            }
        }
        while (changed);

        return labelOf.GroupBy(pair => pair.Value).OrderBy(group => group.Key).Select(group => group.Select(pair => pair.Key).ToList()).ToList();

        bool IsBounded(List<EdgeKey> cut)
        {
            var degree = new Dictionary<(int, int), int>();
            foreach (var edge in cut)
            {
                var (first, second) = GetEnds(edge);
                degree[first] = degree.GetValueOrDefault(first) + 1;
                degree[second] = degree.GetValueOrDefault(second) + 1;
            }

            foreach ((var vertex, int count) in degree)
            {
                if (count % 2 == 0 || IsBar(bars.GetValueOrDefault(vertex)))
                {
                    continue;
                }

                var edge = cut.First(candidate => GetEnds(candidate).First == vertex || GetEnds(candidate).Second == vertex);
                var (low, high) = GetEnds(edge);
                (int stepA, int stepB) = vertex == low ? (low.A - high.A, low.B - high.B) : (high.A - low.A, high.B - low.B);
                if (!Enumerable.Range(1, MaxHoleCells).Any(distance => IsBar(bars.GetValueOrDefault((vertex.Item1 + distance * stepA, vertex.Item2 + distance * stepB)))))
                {
                    return false;
                }
            }

            return true;
        }

        // Keep a cut that ends on a wall corner on one side and runs into a straight wall on the other
        bool IsPassage(List<EdgeKey> cut)
        {
            var degree = new Dictionary<(int, int), int>();
            foreach (var edge in cut)
            {
                var (first, second) = GetEnds(edge);
                degree[first] = degree.GetValueOrDefault(first) + 1;
                degree[second] = degree.GetValueOrDefault(second) + 1;
            }

            var ends = degree.Where(pair => pair.Value % 2 == 1).Select(pair => bars.GetValueOrDefault(pair.Key)).ToArray();
            return ends.Length == 2 && ends.Any(IsBar) && ends.All(end => end.Along + end.Across > 0);
        }

        bool IsNarrow(List<EdgeKey> cut, int label, int other) =>
            cut.Count <= MaxPassageShare * Math.Min(MeasureWidth(label), MeasureWidth(other));

        int MeasureWidth(int label)
        {
            var cells = labelOf.Where(pair => pair.Value == label).Select(pair => pair.Key).ToArray();
            return Math.Min(cells.Max(cell => cell.U) - cells.Min(cell => cell.U), cells.Max(cell => cell.V) - cells.Min(cell => cell.V)) + 1;
        }


        static bool IsBar((int Along, int Across) bar) => bar.Along + bar.Across > 0 && bar is not { Along: 2, Across: 0 } && bar is not { Along: 0, Across: 2 };

        bool TrySnap(int grower, int shrinker)
        {
            var trial = new Dictionary<Cell, int>(labelOf);
            for (int layer = 1; layer <= MaxSnapCells; layer++)
            {
                var next = new List<Cell>();
                foreach ((var cell, int owner) in trial)
                {
                    if (owner != shrinker)
                    {
                        continue;
                    }

                    foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                    {
                        if (trial.TryGetValue(neighbor, out int found) && found == grower && !edges.ContainsKey(edge))
                        {
                            next.Add(cell);
                            break;
                        }
                    }
                }

                if (next.Count == 0 || next.Count == trial.Count(pair => pair.Value == shrinker))
                {
                    return false;
                }

                foreach (var cell in next)
                {
                    trial[cell] = grower;
                }

                var cut = new List<EdgeKey>();
                foreach ((var cell, int owner) in trial)
                {
                    if (owner != grower)
                    {
                        continue;
                    }

                    foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                    {
                        if (trial.TryGetValue(neighbor, out int other) && other == shrinker && !edges.ContainsKey(edge))
                        {
                            cut.Add(edge);
                        }
                    }
                }

                if (cut.Count > 0 && IsBounded(cut))
                {
                    labelOf.Clear();
                    foreach ((var cell, int owner) in trial)
                    {
                        labelOf[cell] = owner;
                    }

                    return true;
                }
            }

            return false;
        }
    }

    private static List<(List<Cell> Piece, bool IsRoom)> SplitNeck(List<Cell> region, Dictionary<EdgeKey, EdgeKind> edges, int minRoomCells, int minCorridorCells)
    {
        int minRadius = (minRoomCells + 1) / 2;
        if (region.Count < 4 * minRoomCells * minRoomCells)
        {
            return [];
        }

        var set = region.ToHashSet();
        var depth = MeasureDepth(set, edges);
        int radius = Math.Max(minRadius, (int)MathF.Ceiling(NeckRatio * depth.Values.Max()));
        var reach = new Dictionary<Cell, int>();
        var buckets = new SortedDictionary<int, List<Cell>>(Comparer<int>.Create((left, right) => right.CompareTo(left)));
        foreach ((var cell, int cellDepth) in depth)
        {
            if (cellDepth >= radius)
            {
                Raise(cell, cellDepth - 1);
            }
        }

        while (buckets.Count > 0)
        {
            (int level, var cells) = buckets.First();
            buckets.Remove(level);
            if (level == 0)
            {
                continue;
            }

            foreach (var cell in cells.Where(cell => reach[cell] == level))
            {
                foreach (var neighbor in GetRing(cell))
                {
                    if (set.Contains(neighbor) && reach.GetValueOrDefault(neighbor, -1) < level - 1)
                    {
                        Raise(neighbor, level - 1);
                    }
                }
            }
        }

        var rooms = GetComponents(set.Where(reach.ContainsKey).ToHashSet(), edges);
        var strips = GetComponents(set.Where(cell => !reach.ContainsKey(cell)).ToHashSet(), edges);
        var pieces = rooms.Select(room => (Piece: room.ToList(), IsRoom: true)).ToList();
        var roomOf = new Dictionary<Cell, int>();
        for (int roomIndex = 0; roomIndex < rooms.Count; roomIndex++)
        {
            foreach (var cell in rooms[roomIndex])
            {
                roomOf[cell] = roomIndex;
            }
        }

        foreach (var strip in strips)
        {
            var touches = new Dictionary<int, int>();
            foreach (var cell in strip)
            {
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    if (!edges.ContainsKey(edge) && roomOf.TryGetValue(neighbor, out int room))
                    {
                        touches[room] = touches.GetValueOrDefault(room) + 1;
                    }
                }
            }

            int length = Math.Max(strip.Max(cell => cell.U) - strip.Min(cell => cell.U), strip.Max(cell => cell.V) - strip.Min(cell => cell.V)) + 1;
            int stripDepth = strip.Max(cell => depth[cell]);
            bool corridor = touches.Count != 1 && length >= minCorridorCells && length >= 2.5f * (2 * stripDepth - 1);
            bool large = stripDepth >= minRadius && strip.Count >= 4 * minRoomCells * minRoomCells;
            if (corridor || large || touches.Count == 0)
            {
                pieces.Add((strip.ToList(), large && !corridor));
            }
            else
            {
                pieces[touches.MaxBy(touch => touch.Value).Key].Piece.AddRange(strip);
            }
        }

        return pieces;

        void Raise(Cell cell, int level)
        {
            reach[cell] = level;
            if (!buckets.TryGetValue(level, out var bucket))
            {
                buckets[level] = bucket = [];
            }

            bucket.Add(cell);
        }
    }

    private static Dictionary<Cell, int> MeasureDepth(HashSet<Cell> set, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var depth = new Dictionary<Cell, int>();
        var queue = new Queue<Cell>();
        foreach (var cell in set)
        {
            bool rim = GetRing(cell).Any(neighbor => !set.Contains(neighbor));
            foreach (var (_, edge) in EdgeKey.GetNeighbors(cell))
            {
                rim |= edges.ContainsKey(edge);
            }

            if (rim)
            {
                depth[cell] = 1;
                queue.Enqueue(cell);
            }
        }

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var neighbor in GetRing(cell))
            {
                if (set.Contains(neighbor) && !depth.ContainsKey(neighbor))
                {
                    depth[neighbor] = depth[cell] + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return depth;
    }

    // Drop the strips of each room's outline too narrow for a square of outline cells and fill its bites and unwalled holes as narrow
    private static void SmoothOutlines(List<List<Cell>> places, Dictionary<Cell, int> placeOf, HashSet<Cell> floor, Dictionary<EdgeKey, EdgeKind> edges)
    {
        for (int index = 0; index < places.Count; index++)
        {
            var place = places[index].ToHashSet();
            var covered = new HashSet<Cell>();
            foreach (var cell in place)
            {
                if (IsSquareCorner(cell, place))
                {
                    covered.UnionWith(GetSquare(cell));
                }
            }

            var strips = place.Where(cell => !covered.Contains(cell)).ToHashSet();
            foreach (var strip in GetComponents(strips, edges))
            {
                var rest = place.Where(cell => !strip.Contains(cell)).ToHashSet();
                if (rest.Count == 0 || rest.Flood([rest.First()], _ => false).Count < rest.Count || strip.Any(cell => IsPassage(cell, index, placeOf, edges)))
                {
                    continue;
                }

                place = rest;
                foreach (var cell in strip)
                {
                    placeOf.Remove(cell);
                    floor.Remove(cell);
                }
            }

            var grown = place.SelectMany(cell => GetRing(cell).Append(cell)).ToHashSet();
            var bites = grown
                .Where(cell => !place.Contains(cell) && !placeOf.ContainsKey(cell) && GetRing(cell).All(grown.Contains))
                .ToHashSet();

            var outside = GetOutside(place);
            foreach (var bite in GetComponents(bites, []))
            {
                if (bite.Any(cell => GetRing(cell).Any(neighbor => placeOf.TryGetValue(neighbor, out int other) && other != index))
                    || (!bite.Overlaps(outside) && bite.Any(cell => IsWalled(cell, edges))))
                {
                    continue;
                }

                place.UnionWith(bite);
                foreach (var cell in bite)
                {
                    placeOf[cell] = index;
                    floor.Add(cell);
                }
            }

            int alignments = 0;
            while (alignments < MaxStepAlignments && AlignStep(place, index, placeOf, floor, edges))
            {
                alignments++;
            }

            places[index] = place.ToList();
        }
    }

    // Line up one side of the outline where it steps by less than a square of outline cells, moving the shorter run to the longer or else the longer to the shorter
    private static bool AlignStep(HashSet<Cell> place, int index, Dictionary<Cell, int> placeOf, HashSet<Cell> floor, Dictionary<EdgeKey, EdgeKind> edges)
    {
        foreach (bool alongU in (bool[])[true, false])
        {
            var runs = new SortedDictionary<int, (int Low, int High)>();
            foreach (var group in place.GroupBy(cell => alongU ? cell.V : cell.U))
            {
                int low = group.Min(cell => alongU ? cell.U : cell.V), high = group.Max(cell => alongU ? cell.U : cell.V);
                if (high - low + 1 == group.Count())
                {
                    runs[group.Key] = (low, high);
                }
            }

            foreach (bool lowSide in (bool[])[true, false])
            {
                var segments = new List<(int First, int Last, int End)>();
                foreach ((int line, var run) in runs)
                {
                    int end = lowSide ? run.Low : run.High;
                    if (segments.Count > 0 && segments[^1].Last == line - 1 && segments[^1].End == end)
                    {
                        segments[^1] = (segments[^1].First, line, end);
                    }
                    else
                    {
                        segments.Add((line, line, end));
                    }
                }

                for (int segment = 0; segment + 1 < segments.Count; segment++)
                {
                    var (one, two) = (segments[segment], segments[segment + 1]);
                    int step = Math.Abs(one.End - two.End);
                    if (two.First != one.Last + 1 || step == 0 || step >= OutlineCells)
                    {
                        continue;
                    }

                    var (shorter, longer) = one.Last - one.First <= two.Last - two.First ? (one, two) : (two, one);
                    if (TryMoveEnd(place, index, placeOf, floor, edges, runs, shorter, longer.End, alongU, lowSide)
                        || TryMoveEnd(place, index, placeOf, floor, edges, runs, longer, shorter.End, alongU, lowSide))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryMoveEnd(HashSet<Cell> place, int index, Dictionary<Cell, int> placeOf, HashSet<Cell> floor, Dictionary<EdgeKey, EdgeKind> edges, SortedDictionary<int, (int Low, int High)> runs,
        (int First, int Last, int End) segment, int target, bool alongU, bool lowSide)
    {
        bool grows = lowSide ? target < segment.End : target > segment.End;
        var cells = new List<Cell>();
        for (int line = segment.First; line <= segment.Last; line++)
        {
            for (int position = Math.Min(segment.End, target); position <= Math.Max(segment.End, target); position++)
            {
                if (position != (grows ? segment.End : target))
                {
                    cells.Add(alongU ? new Cell(position, line) : new Cell(line, position));
                }
            }

            var run = runs[line];
            if (!grows && (lowSide ? run.High - target : target - run.Low) + 1 < OutlineCells)
            {
                return false;
            }
        }

        if (grows)
        {
            if (cells.Any(cell => (placeOf.TryGetValue(cell, out int owner) && owner != index)
                                  || GetRing(cell).Any(neighbor => placeOf.TryGetValue(neighbor, out int other) && other != index)))
            {
                return false;
            }

            foreach (var cell in cells)
            {
                place.Add(cell);
                placeOf[cell] = index;
                floor.Add(cell);
            }

            return true;
        }

        var rest = place.Except(cells).ToHashSet();
        if (rest.Count == 0 || rest.Flood([rest.First()], _ => false).Count < rest.Count || cells.Any(cell => IsPassage(cell, index, placeOf, edges)))
        {
            return false;
        }

        place.ExceptWith(cells);
        foreach (var cell in cells)
        {
            placeOf.Remove(cell);
            floor.Remove(cell);
        }

        return true;
    }

    // Keep the cells a door, an opening or any other passage leaves from
    private static bool IsPassage(Cell cell, int index, Dictionary<Cell, int> placeOf, Dictionary<EdgeKey, EdgeKind> edges)
    {
        foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
        {
            bool walled = edges.TryGetValue(edge, out var kind) && kind == EdgeKind.Wall;
            if (!walled && (edges.ContainsKey(edge) || (placeOf.TryGetValue(neighbor, out int other) && other != index)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSquareCorner(Cell corner, HashSet<Cell> cells) => GetSquare(corner).All(cells.Contains);

    private static IEnumerable<Cell> GetSquare(Cell corner)
    {
        for (int du = 0; du < OutlineCells; du++)
        {
            for (int dv = 0; dv < OutlineCells; dv++)
            {
                yield return new Cell(corner.U + du, corner.V + dv);
            }
        }
    }

    private static bool IsWalled(Cell cell, Dictionary<EdgeKey, EdgeKind> edges)
    {
        foreach (var (_, edge) in EdgeKey.GetNeighbors(cell))
        {
            if (edges.ContainsKey(edge))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<Cell> GetOutside(HashSet<Cell> place)
    {
        int minU = place.Min(cell => cell.U) - 1, maxU = place.Max(cell => cell.U) + 1;
        int minV = place.Min(cell => cell.V) - 1, maxV = place.Max(cell => cell.V) + 1;
        var free = new HashSet<Cell>();
        for (int u = minU; u <= maxU; u++)
        {
            for (int v = minV; v <= maxV; v++)
            {
                var cell = new Cell(u, v);
                if (!place.Contains(cell))
                {
                    free.Add(cell);
                }
            }
        }

        return free.Flood([new Cell(minU, minV)], _ => false);
    }

    private static Cell[] GetRing(Cell cell) =>
    [
        cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 },
        new(cell.U + 1, cell.V + 1), new(cell.U + 1, cell.V - 1), new(cell.U - 1, cell.V + 1), new(cell.U - 1, cell.V - 1)
    ];

    private static List<HashSet<Cell>> GetComponents(HashSet<Cell> cells, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var components = new List<HashSet<Cell>>();
        var seen = new HashSet<Cell>();
        foreach (var cell in cells)
        {
            if (seen.Contains(cell))
            {
                continue;
            }

            var component = cells.Flood([cell], edges.ContainsKey);
            seen.UnionWith(component);
            components.Add(component);
        }

        return components;
    }

    private static void DropEmpty(List<List<Cell>> places, Dictionary<Cell, int> placeOf, HashSet<Cell> floor, IReadOnlySet<Cell> content, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var empty = Enumerable.Range(0, places.Count)
            .Where(index => places[index].Count(content.Contains) < Math.Max(1, places[index].Count / ContentSpacingCells))
            .ToHashSet();

        var links = FindPlaceLinks(places, placeOf, edges);
        var dropped = new HashSet<List<Cell>>();
        foreach (var group in GroupLinked(empty, links))
        {
            if (!IsBridge(group, links, empty))
            {
                dropped.UnionWith(group.Select(index => places[index]));
            }
        }

        RemovePlaces(places, placeOf, floor, dropped.Contains);
    }

    private static List<HashSet<int>> GroupLinked(HashSet<int> members, Dictionary<int, HashSet<int>> links)
    {
        var groups = new List<HashSet<int>>();
        var left = new HashSet<int>(members);
        while (left.Count > 0)
        {
            int first = left.First();
            var group = new HashSet<int> { first };
            var queue = new Queue<int>([first]);
            while (queue.Count > 0)
            {
                foreach (int next in links[queue.Dequeue()].Where(next => left.Contains(next) && group.Add(next)))
                {
                    queue.Enqueue(next);
                }
            }

            left.ExceptWith(group);
            groups.Add(group);
        }

        return groups;
    }

    private static Dictionary<int, HashSet<int>> FindPlaceLinks(List<List<Cell>> places, Dictionary<Cell, int> placeOf, Dictionary<EdgeKey, EdgeKind> edges)
    {
        var links = Enumerable.Range(0, places.Count).ToDictionary(index => index, _ => new HashSet<int>());
        foreach ((var cell, int index) in placeOf)
        {
            foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (placeOf.TryGetValue(neighbor, out int other) && other != index && !(edges.TryGetValue(edge, out var kind) && kind.BlocksWalking))
                {
                    links[index].Add(other);
                }
            }
        }

        return links;
    }

    // Keep empty places whose kept neighbors reach each other only through them
    private static bool IsBridge(HashSet<int> group, Dictionary<int, HashSet<int>> links, HashSet<int> empty)
    {
        int[] kept = group
            .SelectMany(index => links[index])
            .Where(other => !empty.Contains(other))
            .Distinct()
            .ToArray();

        if (kept.Length < 2)
        {
            return false;
        }

        var reached = new HashSet<int> { kept[0] };
        var queue = new Queue<int>([kept[0]]);
        while (queue.Count > 0)
        {
            foreach (int next in links[queue.Dequeue()].Where(next => !group.Contains(next) && reached.Add(next)))
            {
                queue.Enqueue(next);
            }
        }

        return !kept.All(reached.Contains);
    }

    // Drop the floor strips under back walls and roofs that small wall doors open onto
    private static bool IsHollowStrip(List<Cell> place, IReadOnlySet<Cell> furnished)
    {
        int width = place.Max(cell => cell.U) - place.Min(cell => cell.U) + 1;
        int depth = place.Max(cell => cell.V) - place.Min(cell => cell.V) + 1;
        return !place.Any(furnished.Contains) && Math.Min(width, depth) <= MaxStripCells && Math.Max(width, depth) >= Math.Max(MinStripCells, MinStripRatio * Math.Min(width, depth));
    }

    private static void RemovePlaces(List<List<Cell>> places, Dictionary<Cell, int> placeOf, HashSet<Cell> floor, Func<List<Cell>, bool> drop)
    {
        foreach (var place in places.Where(drop))
        {
            foreach (var cell in place)
            {
                floor.Remove(cell);
                placeOf.Remove(cell);
            }

            place.Clear();
        }

        var kept = places.Where(place => place.Count > 0).ToList();
        places.Clear();
        places.AddRange(kept);
        for (int index = 0; index < places.Count; index++)
        {
            foreach (var cell in places[index])
            {
                placeOf[cell] = index;
            }
        }
    }

    private static void MergeSmall(List<List<Cell>> places, Dictionary<Cell, int> placeOf, Dictionary<EdgeKey, EdgeKind> edges, HashSet<EdgeKey> openings,
        int minPlaceCells, int pocketCells)
    {
        bool merged = true;
        while (merged)
        {
            merged = false;
            foreach (int index in Enumerable.Range(0, places.Count).Where(IsSmall).OrderBy(placeIndex => places[placeIndex].Count).ToArray())
            {
                if (!IsSmall(index))
                {
                    continue;
                }

                var shared = new Dictionary<int, int>();
                foreach (var cell in places[index])
                {
                    foreach (var (n, k) in EdgeKey.GetNeighbors(cell))
                    {
                        if ((openings.Contains(k) || (edges.TryGetValue(k, out var kind) && kind == EdgeKind.FenceFrame)) && placeOf.TryGetValue(n, out int other) && other != index)
                        {
                            shared[other] = shared.GetValueOrDefault(other) + 1;
                        }
                    }
                }

                if (shared.Count == 0)
                {
                    continue;
                }

                int target = shared.MaxBy(kv => kv.Value).Key;
                foreach (var cell in places[index])
                {
                    placeOf[cell] = target;
                    foreach (var (n, k) in EdgeKey.GetNeighbors(cell))
                    {
                        if (placeOf.TryGetValue(n, out int other) && other == target)
                        {
                            openings.Remove(k);
                        }
                    }
                }

                places[target].AddRange(places[index]);
                places[index].Clear();
                merged = true;
            }
        }

        bool IsSmall(int index) => places[index].Count > 0 && (places[index].Count < Math.Max(minPlaceCells, pocketCells) || MeasureDepth(places[index].ToHashSet(), edges).Values.Max() <= 2);

        var kept = places.Where(p => p.Count > 0).ToList();
        places.Clear();
        places.AddRange(kept);
        for (int index = 0; index < places.Count; index++)
        {
            foreach (var cell in places[index])
            {
                placeOf[cell] = index;
            }
        }
    }
}

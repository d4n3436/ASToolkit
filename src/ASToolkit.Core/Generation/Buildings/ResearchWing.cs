using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using static ASToolkit.Core.Generation.Buildings.ResearchPart;

namespace ASToolkit.Core.Generation.Buildings;

// Draws a research wing in units, a hall entered at a T with one block on its far side holding a ladder of two rungs and a cross passage, a ring of
// pieces around an empty court, labs on the ring and an office block opening back onto the hall; each piece takes jittered rectangles off a nominal
// grid
internal sealed class ResearchWing
{
    private const int Attempts = 400;

    private static readonly Cell[] Sides = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private static readonly Cell[] Forward = [new(1, 0), new(0, 1)];

    private static readonly (ResearchPart A, ResearchPart B)[] DesignedPairs =
    [
        (Hall, UpperRung), (Hall, LowerRung), (UpperRung, LadderCross), (LowerRung, LadderCross), (Hall, FrontOffice), (LowerRung, FrontOffice), (Hall, EndStorage),
        (EndStorage, BackOffice), (Storage, HallLab), (UpperRung, Storage), (Storage, Office), (Office, Depot), (Depot, ShortPassage), (ShortPassage, Cross),
        (Cross, LadderCross), (LadderCross, UpperRung), (Hall, HallLab)
    ];

    private static readonly (ResearchPart A, ResearchPart B)[] JammedPairs = [(Hall, LowerRung), (Hall, HallLab)];

    private static readonly (ResearchPart A, ResearchPart B)[] ExtraPairs = [(EndStorage, LowerOffice), (Depot, DepotLab)];

    private readonly GrammarRandom _random;

    public ResearchWing(GrammarRandom random)
    {
        _random = random;
    }

    public ResearchLayout? Draw(int rooms)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var layout = TryDraw(rooms);
            if (layout is not null)
            {
                return layout;
            }
        }

        return null;
    }

    private ResearchLayout? TryDraw(int rooms)
    {
        int hallWidth = _random.Inclusive(3, 4);
        int[] r = Cumulate(0, _random.Inclusive(6, 8), _random.Inclusive(3, 4), _random.Inclusive(7, 11), _random.Inclusive(3, 4), _random.Inclusive(6, 8));
        int[] c = Cumulate(hallWidth, _random.Inclusive(6, 7), _random.Inclusive(6, 8), _random.Inclusive(6, 9), _random.Inclusive(12, 14));
        int bar = _random.Inclusive(4, 5);
        int barX = c[1] + _random.Inclusive(0, Math.Max(0, c[2] - c[1] - bar));
        int armY = _random.Inclusive(r[2] + 1, Math.Max(r[2] + 1, r[3] - 4));
        int depotLow = Math.Min(r[2] + _random.Inclusive(2, Math.Max(2, (r[3] - r[2]) / 2)), 160 / (c[4] - c[3]) + 1);
        int shortWidth = _random.Inclusive(4, 6);
        int shortHeight = _random.Inclusive(3, 4);
        int east = c[3] + _random.Inclusive(0, Math.Max(0, c[4] - c[3] - 5));
        const int split = 5;
        int endWidth = _random.Inclusive(5, 6);
        // Add rooms past the seven the frame holds in order, two labs under the cross, an office under the end storage, a lab off the depot and a
        // third lab under the cross
        int extra = Math.Clamp(rooms - 7, 1, 5);
        int crossLabs = extra >= 5 ? 3 : Math.Min(2, extra);

        // Lay the passages first, the wing's frame, then the rooms on the cells they leave
        var pieces = new List<ResearchPiece>();
        Add(pieces, UpperRung, [UnitRect.FromEdges(c[0], r[1], c[2], r[2])]);
        Add(pieces, LadderCross, [UnitRect.FromEdges(barX, r[2], barX + bar, r[4]), UnitRect.FromEdges(c[1], armY, c[2], armY + 3)]);
        Add(pieces, LowerRung, [UnitRect.FromEdges(c[0], r[3], barX, r[4])]);
        Add(pieces, ShortPassage, [UnitRect.FromEdges(c[3], depotLow, c[3] + shortWidth, depotLow + shortHeight)]);
        Add(pieces, Cross, [UnitRect.FromEdges(c[2], r[3], c[4], r[4]), UnitRect.FromEdges(east, depotLow + shortHeight, east + 5, r[4])]);
        Add(pieces, Depot, [UnitRect.FromEdges(c[3], r[0], c[4], depotLow), UnitRect.FromEdges(c[3] + shortWidth, depotLow, c[4], depotLow + _random.Inclusive(2, 4))]);
        Add(pieces, Office, [UnitRect.FromEdges(c[2], r[0], c[3], r[1]), UnitRect.FromEdges(c[2], r[1], c[2] + _random.Inclusive(4, 5), r[1] + _random.Inclusive(2, 4))]);
        Add(pieces, Storage, [UnitRect.FromEdges(c[1], r[0], c[2], r[1])]);
        Add(pieces, HallLab, [UnitRect.FromEdges(c[0], r[0], c[1], r[1])]);
        Add(pieces, FrontOffice, [UnitRect.FromEdges(c[0], r[4], c[1], r[4] + split)]);
        Add(pieces, EndStorage, [UnitRect.FromEdges(c[0], r[4] + split, c[0] + endWidth, r[4] + split + 5)]);
        Add(pieces, BackOffice, [UnitRect.FromEdges(c[0] + endWidth, r[4] + split - _random.Inclusive(0, 2), c[2], r[4] + split + 5)]);
        int far = c[4] - _random.Inclusive(0, 2);
        int start = crossLabs == 1 && _random.Next() < 0.5f ? c[3] : c[2];
        for (int lab = 0; lab < crossLabs; lab++)
        {
            int end = lab == crossLabs - 1 ? far : start + Math.Max(6, (far - start) / (crossLabs - lab) + _random.Inclusive(-1, 1));
            end = crossLabs == 1 && start == c[2] ? start + _random.Inclusive(6, 8) : end;
            Add(pieces, CrossLab, [UnitRect.FromEdges(start, r[4], end, r[5] - _random.Inclusive(0, 1))]);
            start = end;
        }

        if (extra >= 3)
        {
            Add(pieces, LowerOffice, [UnitRect.FromEdges(c[0], r[4] + split + 5, c[0] + _random.Inclusive(7, 9), r[4] + split + 10)]);
        }

        if (extra >= 4)
        {
            int top = _random.Inclusive(0, 2);
            Add(pieces, DepotLab, [UnitRect.FromEdges(c[4], top, c[4] + _random.Inclusive(6, 7), top + _random.Inclusive(6, 8))]);
        }

        var taken = new HashSet<Cell>();
        foreach (var piece in pieces)
        {
            var cells = new HashSet<Cell>();
            foreach (var rect in piece.Nominal)
            {
                cells.UnionWith(Jitter(rect, hallWidth, piece.IsPassage ? 3 : 4, piece.IsPassage || piece.Part == Depot).ToCells());
            }

            cells.RemoveWhere(cell => cell.U < hallWidth || taken.Contains(cell));
            piece.Cells = Largest(Trim(cells, piece.IsPassage ? 3 : 4));
            if (piece.Cells.Count < (piece.IsPassage ? 12 : 20))
            {
                return null;
            }

            taken.UnionWith(piece.Cells);
        }

        int foot = taken.Max(cell => cell.V) + 1;
        var hall = new ResearchPiece(Hall, []) { Cells = UnitRect.FromEdges(0, 0, hallWidth, foot).ToCells() };
        pieces.Insert(0, hall);
        taken.UnionWith(hall.Cells);

        foreach (var piece in _random.Shuffled(pieces.Where(piece => !piece.IsPassage && piece.Part != Hall)).Take(_random.Inclusive(2, 4)))
        {
            int w = _random.Inclusive(2, 4);
            int h = _random.Inclusive(2, 4);
            int u = _random.Next() < 0.5f ? piece.Cells.Min(cell => cell.U) : piece.Cells.Max(cell => cell.U) - w + 1;
            int v = _random.Next() < 0.5f ? piece.Cells.Min(cell => cell.V) : piece.Cells.Max(cell => cell.V) - h + 1;
            TryReplace(piece, piece.Cells.Except(UnitRect.FromEdges(u, v, u + w, v + h).ToCells()).ToHashSet(), 4);
        }

        var depot = pieces.First(piece => piece.Part == Depot);
        int right = depot.Cells.Max(cell => cell.U);
        int crown = depot.Cells.Min(cell => cell.V);
        if (depot.Cells.Count == (right - depot.Cells.Min(cell => cell.U) + 1) * (depot.Cells.Max(cell => cell.V) - crown + 1)
            && !TryReplace(depot, depot.Cells.Except(UnitRect.FromEdges(right - _random.Inclusive(3, 4) + 1, crown, right + 1, crown + _random.Inclusive(3, 4)).ToCells()).ToHashSet(), 4))
        {
            return null;
        }

        var designed = Designed(pieces);
        foreach ((int a, int b) in designed)
        {
            if (Wall(pieces[a].Cells, pieces[b].Cells).Count < 3 && !ReachAcross(pieces, a, b, taken))
            {
                return null;
            }
        }

        StepWalls(pieces);
        StandClumpsApart(pieces, designed);
        CloseSlits(pieces, designed);
        var doors = designed.ToList();
        for (int a = 1; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                if (!designed.Contains(new RoomPair(a, b)) && !pieces[a].IsPassage && !pieces[b].IsPassage && ClumpOf(pieces[a].Part) == ClumpOf(pieces[b].Part)
                    && Wall(pieces[a].Cells, pieces[b].Cells).Count >= 3)
                {
                    doors.Add(new RoomPair(a, b));
                }
            }
        }

        if (HasSlit(pieces, doors))
        {
            return null;
        }

        if (designed.Any(pair => Wall(pieces[pair.A].Cells, pieces[pair.B].Cells).Count < 3) || pieces.Any(piece => !piece.Cells.IsConnected() || !piece.Cells.IsThick(3)))
        {
            return null;
        }

        var jammed = JammedPairs
            .Select(pair => new RoomPair(pieces.FindIndex(piece => piece.Part == pair.A), pieces.FindIndex(piece => piece.Part == pair.B)))
            .ToArray();

        return new ResearchLayout
        {
            Pieces = pieces,
            Doors = doors.Where(door => !jammed.Contains(door)).ToArray(),
            Jammed = jammed,
            End = pieces.FindIndex(piece => piece.Part == EndStorage)
        };
    }

    private static int[] Cumulate(params int[] steps)
    {
        for (int index = 1; index < steps.Length; index++)
        {
            steps[index] += steps[index - 1];
        }

        return steps;
    }

    private static void Add(List<ResearchPiece> pieces, ResearchPart part, UnitRect[] nominal) => pieces.Add(new ResearchPiece(part, nominal));

    // Moves each edge of a nominal rectangle out by up to two units or in by one, only in for a piece that keeps its size and never on the hall's
    // edge
    private UnitRect Jitter(UnitRect rect, int hallWidth, int minSide, bool keepsSize)
    {
        int grow = keepsSize ? 0 : 2;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            int u0 = rect.U == hallWidth ? rect.U : rect.U + _random.Inclusive(-grow, 1);
            int u1 = rect.EndU + _random.Inclusive(-1, grow);
            int v0 = Math.Max(0, rect.V + _random.Inclusive(-grow, 1));
            int v1 = rect.EndV + _random.Inclusive(-1, grow);
            if (u1 - u0 >= minSide && v1 - v0 >= minSide)
            {
                return UnitRect.FromEdges(u0, v0, u1, v1);
            }
        }

        return rect;
    }

    private static List<RoomPair> Designed(List<ResearchPiece> pieces)
    {
        var labs = Enumerable.Range(0, pieces.Count).Where(index => pieces[index].Part == CrossLab).Select(index => new RoomPair(Of(Cross), index));
        return DesignedPairs
            .Select(pair => new RoomPair(Of(pair.A), Of(pair.B)))
            .Concat(labs)
            .Concat(ExtraPairs.Where(pair => Of(pair.B) >= 0).Select(pair => new RoomPair(Of(pair.A), Of(pair.B))))
            .ToList();
        
        int Of(ResearchPart part) => pieces.FindIndex(piece => piece.Part == part);
    }

    // Reaches a door across a gap of one to three units with a three-wide stub from the smaller piece, never from the hall
    private static bool ReachAcross(List<ResearchPiece> pieces, int a, int b, HashSet<Cell> taken)
    {
        (int from, int to) = pieces[a].Part == Hall || (pieces[b].Part != Hall && pieces[b].Cells.Count < pieces[a].Cells.Count) ? (b, a) : (a, b);
        HashSet<Cell> best = [];
        int bestGap = int.MaxValue;
        foreach (var cell in pieces[from].Cells.OrderBy(cell => cell.U).ThenBy(cell => cell.V))
        {
            foreach (var side in Sides)
            {
                var across = new Cell(Math.Abs(side.V), Math.Abs(side.U));
                if (!Enumerable.Range(0, 3).All(k => pieces[from].Cells.Contains(new Cell(cell.U + across.U * k, cell.V + across.V * k))))
                {
                    continue;
                }

                for (int gap = 1; gap <= 3 && gap < bestGap; gap++)
                {
                    var stub = Enumerable.Range(1, gap)
                        .SelectMany(step => Enumerable.Range(0, 3).Select(k => new Cell(cell.U + side.U * step + across.U * k, cell.V + side.V * step + across.V * k)))
                        .ToHashSet();

                    if (stub.Overlaps(taken))
                    {
                        break;
                    }

                    if (Enumerable.Range(0, 3).All(k => pieces[to].Cells.Contains(new Cell(cell.U + side.U * (gap + 1) + across.U * k, cell.V + side.V * (gap + 1) + across.V * k))))
                    {
                        best = stub;
                        bestGap = gap;
                        break;
                    }
                }
            }
        }

        if (best.Count == 0)
        {
            return false;
        }

        pieces[from].Cells.UnionWith(best);
        taken.UnionWith(best);
        return true;
    }

    // Moves part of a wall two rooms share by two or three units, a tab of one into the other, both left four wide
    private void StepWalls(List<ResearchPiece> pieces)
    {
        var rooms = pieces.Where(piece => !piece.IsPassage && piece.Part != Hall).ToArray();
        for (int attempt = _random.Inclusive(10, 14); attempt > 0; attempt--)
        {
            var a = _random.Choice(rooms);
            var b = _random.Choice(rooms);
            var wall = a == b ? [] : Wall(a.Cells, b.Cells);
            if (wall.Count < 6)
            {
                continue;
            }

            var edge = _random.Choice(wall);
            int depth = _random.Inclusive(2, 3);
            int span = _random.Inclusive(3, 5);
            var tab = edge.Side.U != 0
                ? UnitRect.FromEdges(Math.Min(edge.Cell.U + edge.Side.U, edge.Cell.U + edge.Side.U * depth), edge.Cell.V, Math.Max(edge.Cell.U + edge.Side.U, edge.Cell.U + edge.Side.U * depth) + 1, edge.Cell.V + span).ToCells()
                : UnitRect.FromEdges(edge.Cell.U, Math.Min(edge.Cell.V + edge.Side.V, edge.Cell.V + edge.Side.V * depth), edge.Cell.U + span, Math.Max(edge.Cell.V + edge.Side.V, edge.Cell.V + edge.Side.V * depth) + 1).ToCells();

            if (tab.IsSubsetOf(b.Cells) && TryReplace(b, b.Cells.Except(tab).ToHashSet(), 4))
            {
                a.Cells.UnionWith(tab);
            }
        }
    }

    // Stands rooms of different clumps apart along their shared wall, a designed door keeping a three-unit stub and the gap cut from the room
    private void StandClumpsApart(List<ResearchPiece> pieces, List<RoomPair> designed)
    {
        for (int a = 1; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                if (ClumpOf(pieces[a].Part) == ClumpOf(pieces[b].Part) || Wall(pieces[a].Cells, pieces[b].Cells).Count < 3)
                {
                    continue;
                }

                var (cut, other) = pieces[a].IsPassage != pieces[b].IsPassage
                    ? pieces[a].IsPassage ? (pieces[b], pieces[a]) : (pieces[a], pieces[b])
                    : pieces[a].Cells.Count >= pieces[b].Cells.Count ? (pieces[a], pieces[b]) : (pieces[b], pieces[a]);

                var wall = Wall(cut.Cells, other.Cells)
                    .OrderBy(edge => edge.Cell.U)
                    .ThenBy(edge => edge.Cell.V)
                    .ToList();

                var kept = new HashSet<Cell>();
                bool isDoor = designed.Contains(new RoomPair(a, b));
                if (isDoor)
                {
                    if (wall.Count < 5)
                    {
                        continue;
                    }

                    int start = _random.Inclusive(1, wall.Count - 4);
                    kept.UnionWith(wall.Skip(start).Take(3).Select(edge => edge.Cell));
                }

                // Stand a pair with a door a unit apart and a pair without one two units apart, else leave them touching
                int width = isDoor ? 1 : 2;
                var gap = wall
                    .Where(edge => !kept.Contains(edge.Cell))
                    .SelectMany(edge => Enumerable.Range(0, width).Select(step => new Cell(edge.Cell.U - edge.Side.U * step, edge.Cell.V - edge.Side.V * step)))
                    .Where(cut.Cells.Contains)
                    .ToHashSet();

                TryReplace(cut, cut.Cells.Except(gap).ToHashSet(), 3);
            }
        }
    }

    // Fills a one-unit gap of three units or more between two pieces without a designed door into the smaller one and widens a shorter gap to two
    // units
    private static void CloseSlits(List<ResearchPiece> pieces, List<RoomPair> designed)
    {
        var owner = Owners(pieces);
        var slit = new Dictionary<Cell, (int Into, Cell Along, Cell Toward)>();
        var gaps = owner
            .SelectMany(entry => Forward.Select(side => (Cell: entry.Key + side, Side: side, A: entry.Value)))
            .OrderBy(gap => gap.Cell.U)
            .ThenBy(gap => gap.Cell.V)
            .ThenBy(gap => gap.Side.V);

        foreach ((var cell, var side, int a) in gaps)
        {
            if (owner.ContainsKey(cell) || !owner.TryGetValue(cell + side, out int b) || a == b || designed.Contains(new RoomPair(a, b)))
            {
                continue;
            }

            int into = pieces[a].Part == Hall || (pieces[b].Part != Hall && pieces[b].Cells.Count < pieces[a].Cells.Count) ? b : a;
            slit[cell] = (into, new Cell(side.V, side.U), into == b ? side : new Cell(-side.U, -side.V));
        }

        foreach (var (cell, entry) in slit)
        {
            int run = 1 + Enumerable.Range(1, 2).TakeWhile(step => slit.ContainsKey(new Cell(cell.U + entry.Along.U * step, cell.V + entry.Along.V * step))).Count()
                + Enumerable.Range(1, 2).TakeWhile(step => slit.ContainsKey(new Cell(cell.U - entry.Along.U * step, cell.V - entry.Along.V * step))).Count();
            var piece = pieces[entry.Into];
            if (run >= 3)
            {
                piece.Cells.Add(cell);
            }
            else
            {
                // Widen a short slit to two units from the piece the fill would have gone to
                TryReplace(piece, piece.Cells.Where(kept => kept != cell + entry.Toward).ToHashSet(), piece.IsPassage ? 3 : 4);
            }
        }
    }

    private static bool HasSlit(List<ResearchPiece> pieces, List<RoomPair> doors)
    {
        var owner = Owners(pieces);
        return owner.Keys.Any(cell => Forward.Any(side =>
            !owner.ContainsKey(cell + side)
            && owner.TryGetValue(cell + side * 2, out int other) && other != owner[cell]
            && !doors.Contains(new RoomPair(owner[cell], other))));
    }

    private static Dictionary<Cell, int> Owners(List<ResearchPiece> pieces)
    {
        var owner = new Dictionary<Cell, int>();
        for (int index = 0; index < pieces.Count; index++)
        {
            foreach (var cell in pieces[index].Cells)
            {
                owner[cell] = index;
            }
        }

        return owner;
    }

    private static int ClumpOf(ResearchPart part) => part switch
    {
        HallLab or Storage => 0,
        Office or Depot or DepotLab => 1,
        Cross or CrossLab => 2,
        FrontOffice or EndStorage or BackOffice or LowerOffice => 3,
        _ => 4 + (int)part
    };

    private static bool TryReplace(ResearchPiece piece, HashSet<Cell> cells, int least)
    {
        if (!cells.IsConnected() || !cells.IsThick(least))
        {
            return false;
        }

        piece.Cells = cells;
        return true;
    }

    private static List<WallEdge> Wall(HashSet<Cell> a, HashSet<Cell> b) => a
        .SelectMany(cell => Sides.Where(side => b.Contains(cell + side)).Select(side => new WallEdge(cell, side)))
        .ToList();

    private static HashSet<Cell> Largest(HashSet<Cell> cells) => cells.GetPieces().MaxBy(piece => piece.Count) ?? [];

    // Drops every cell in a run under `least` along either axis until none is left
    private static HashSet<Cell> Trim(HashSet<Cell> cells, int least)
    {
        var kept = new HashSet<Cell>(cells);
        while (kept.Count > 0)
        {
            var thin = kept.Where(cell => Math.Min(Run(kept, cell, 1, 0), Run(kept, cell, 0, 1)) < least).ToArray();
            if (thin.Length == 0)
            {
                break;
            }

            kept.ExceptWith(thin);
        }

        return kept;
    }

    private static int Run(HashSet<Cell> cells, Cell cell, int du, int dv)
    {
        int length = 1;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            for (var next = new Cell(cell.U + du * sign, cell.V + dv * sign); cells.Contains(next); next = new Cell(next.U + du * sign, next.V + dv * sign))
            {
                length++;
            }
        }

        return length;
    }

    private readonly record struct WallEdge(Cell Cell, Cell Side);
}

internal enum ResearchPart
{
    Hall,
    UpperRung,
    LadderCross,
    LowerRung,
    ShortPassage,
    Cross,
    Depot,
    Office,
    Storage,
    HallLab,
    FrontOffice,
    EndStorage,
    BackOffice,
    CrossLab,
    LowerOffice,
    DepotLab
}

internal sealed class ResearchPiece
{
    public ResearchPiece(ResearchPart part, UnitRect[] nominal)
    {
        Part = part;
        Nominal = nominal;
    }

    public ResearchPart Part { get; }

    public IReadOnlyList<UnitRect> Nominal { get; }

    public HashSet<Cell> Cells { get; set; } = [];

    public bool IsPassage => Part is UpperRung or LadderCross or LowerRung or ShortPassage or Cross;

    public RoomType Type => Part switch
    {
        Depot => RoomType.Depot,
        Office or FrontOffice or BackOffice or LowerOffice => RoomType.Office,
        Storage or EndStorage => RoomType.Storage,
        HallLab or CrossLab or DepotLab => RoomType.Lab,
        Hall => RoomType.Hall,
        _ => RoomType.Passage
    };
}

internal sealed class ResearchLayout
{
    public required IReadOnlyList<ResearchPiece> Pieces { get; init; }

    public required IReadOnlyList<RoomPair> Doors { get; init; }

    public required IReadOnlyList<RoomPair> Jammed { get; init; }

    public required int End { get; init; }
}

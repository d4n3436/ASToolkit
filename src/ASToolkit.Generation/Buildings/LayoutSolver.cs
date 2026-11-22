using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Buildings;

internal sealed class LayoutSolver<T>
    where T : notnull
{
    private const int CheckBudget = 400_000;

    private readonly LayoutProblem<T> _problem;
    private readonly Random _random;
    private readonly T[] _rooms;
    private readonly ShapeVariant[][] _variants;
    private readonly bool[,] _isJoined;
    private readonly bool[,] _mayTouch;
    private readonly int[,] _touchLength;
    private readonly int[,] _beyondMargin;
    private readonly bool[,] _keepsClear;
    private readonly DoorSides[,] _doorSides;
    private readonly DoorSides[] _anchorSides;
    private readonly UnitRect[][] _obstacles;
    private readonly int[] _placedVariant;
    private readonly Point[] _placedAt;
    private readonly UnitRect[][] _placedRects;
    private readonly DoorLine[][] _placedLines;
    private int _checks;

    public LayoutSolver(LayoutProblem<T> problem, int seed)
    {
        _problem = problem;
        _random = new Random(seed);
        _rooms = problem.Rooms.Keys.ToArray();
        int count = _rooms.Length;
        var index = _rooms.Index().ToDictionary(pair => pair.Item, pair => pair.Index);

        _variants = _rooms.Select(GetVariants).ToArray();
        _isJoined = new bool[count, count];
        _mayTouch = new bool[count, count];
        _touchLength = new int[count, count];
        _beyondMargin = new int[count, count];
        _keepsClear = new bool[count, count];
        _doorSides = new DoorSides[count, count];
        _anchorSides = new DoorSides[count];
        _placedVariant = new int[count];
        _placedAt = new Point[count];
        _placedRects = new UnitRect[count][];
        _placedLines = new DoorLine[count][];
        Array.Fill(_placedVariant, -1);
        MemoryMarshal.CreateSpan(ref _beyondMargin[0, 0], _beyondMargin.Length).Fill(-1);

        foreach (var (a, b) in problem.Joins)
        {
            _isJoined[index[a], index[b]] = _isJoined[index[b], index[a]] = true;
        }

        foreach (var (a, b) in problem.Joins.Concat(problem.TouchPairs).Concat(problem.MayTouchPairs))
        {
            _mayTouch[index[a], index[b]] = _mayTouch[index[b], index[a]] = true;
        }

        foreach (var (a, b) in problem.TouchPairs)
        {
            int length = Math.Max(_touchLength[index[a], index[b]], problem.TouchLengths.GetValueOrDefault((a, b), problem.TouchLength));
            _touchLength[index[a], index[b]] = _touchLength[index[b], index[a]] = length;
        }

        foreach (var ((room, neighbor), sides) in problem.DoorSides)
        {
            _doorSides[index[room], index[neighbor]] = sides;
            _anchorSides[index[room]] |= sides;
        }

        foreach (var (anchor, room) in problem.BeyondPairs)
        {
            _beyondMargin[index[anchor], index[room]] = _isJoined[index[anchor], index[room]] ? 0 : problem.BeyondMargin;
        }

        foreach (var (anchor, room) in problem.BeyondPairs.Concat(problem.ClearPairs))
        {
            _keepsClear[index[anchor], index[room]] = true;
        }

        _obstacles = _rooms.Select(room => problem.Obstacles.Where(obstacle => EqualityComparer<T>.Default.Equals(obstacle.Anchor, room)).SelectMany(obstacle => obstacle.Rects).ToArray())
            .ToArray();
    }

    private static ReadOnlySpan<DoorSides> FacingSides => [DoorSides.MaxX, DoorSides.MinX, DoorSides.MaxY, DoorSides.MinY];

    public Dictionary<T, PlacedRoom<T>> Solve()
    {
        if (!PlaceRemainingRooms(0))
        {
            return [];
        }

        return Enumerable.Range(0, _rooms.Length).ToDictionary(room => _rooms[room], room =>
        {
            var variant = _variants[room][_placedVariant[room]];
            var at = _placedAt[room];
            return new PlacedRoom<T>(_rooms[room], variant.Turn, variant.Cells.Select(cell => new Cell(cell.U + at.X, cell.V + at.Y)).ToHashSet(),
                _problem.Rooms[_rooms[room]].IsCorridor);
        });
    }

    private ShapeVariant[] GetVariants(T room)
    {
        bool readsSides = _problem.DoorSides.Keys.Any(key => EqualityComparer<T>.Default.Equals(key.Room, room));
        var variants = _problem.Rooms[room].Shapes
            .SelectMany(ShapeVariant.GetTurnedVariants)
            .ToArray();

        return variants.Where((variant, i) => !variants.Take(i).Any(earlier => earlier.HasSameOutlineAndDoors(variant, readsSides))).ToArray();
    }

    private bool PlaceRemainingRooms(int placed)
    {
        if (placed == _rooms.Length)
            return true;

        int room = -1;
        List<LayoutCandidate>? fewest = null;
        for (int next = 0; next < _rooms.Length; next++)
        {
            if (_placedVariant[next] >= 0 || (placed == 0 ? next != 0 : FindPlacedNeighbor(next) < 0))
                continue;

            var fitting = new List<LayoutCandidate>();
            foreach (var candidate in ListDoorAlignedCandidates(next, placed == 0))
            {
                if (++_checks > CheckBudget)
                    return false;

                if (FitsPlacedRooms(next, candidate))
                {
                    fitting.Add(candidate);
                }
            }

            if (fitting.Count == 0)
                return false;

            if (fewest is null || fitting.Count < fewest.Count)
            {
                room = next;
                fewest = fitting;
            }
        }

        if (fewest is null)
        {
            throw new InvalidOperationException($"Room {_rooms[Array.IndexOf(_placedVariant, -1)]} is not joined to any room placed before it.");
        }

        foreach (var candidate in fewest)
        {
            Place(room, candidate);
            if (PlaceRemainingRooms(placed + 1))
                return true;

            _placedVariant[room] = -1;
            if (_checks > CheckBudget)
                return false;
        }

        return false;
    }

    private int FindPlacedNeighbor(int room)
    {
        for (int other = 0; other < _rooms.Length; other++)
        {
            if (_placedVariant[other] >= 0 && _isJoined[room, other])
                return other;
        }

        return -1;
    }

    private List<LayoutCandidate> ListDoorAlignedCandidates(int room, bool isFirst)
    {
        var variants = _variants[room];
        var candidates = new List<LayoutCandidate>();
        if (isFirst)
        {
            candidates.AddRange(Enumerable.Range(0, variants.Length).Select(variant => new LayoutCandidate(variant, Point.Empty)));
        }
        else
        {
            int neighbor = FindPlacedNeighbor(room);
            var seen = new HashSet<LayoutCandidate>();
            for (int variant = 0; variant < variants.Length; variant++)
            {
                foreach (var own in variants[variant].Lines.Where(line => MayOpenToward(room, neighbor, line)))
                {
                    foreach (var other in _placedLines[neighbor].Where(line => line.Faces(own) && MayOpenToward(neighbor, room, line)))
                    {
                        for (int along = other.Low - own.High; along <= other.High - own.Low; along++)
                        {
                            var candidate = new LayoutCandidate(variant, own.RunsAlongV ? new Point(other.At - own.At, along) : new Point(along, other.At - own.At));
                            if (seen.Add(candidate))
                            {
                                candidates.Add(candidate);
                            }
                        }
                    }
                }
            }
        }

        _random.Shuffle(CollectionsMarshal.AsSpan(candidates));
        return candidates;
    }

    private bool MayOpenToward(int room, int neighbor, DoorLine line)
    {
        var sides = _doorSides[room, neighbor];
        return sides == DoorSides.None || (sides & FacingSides[line.OwnFacing]) != 0;
    }

    private bool FitsPlacedRooms(int room, LayoutCandidate candidate)
    {
        var variant = _variants[room][candidate.Variant];
        var at = candidate.Position;
        Span<UnitRect> rects = stackalloc UnitRect[variant.Rects.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            rects[i] = Offset(variant.Rects[i], at);
        }

        for (int other = 0; other < _rooms.Length; other++)
        {
            if (_placedVariant[other] < 0)
                continue;

            var otherRects = _placedRects[other];
            if (!KeepsWallGap(room, rects, other) ||
                (_isJoined[room, other] && !LinesUpDoor(room, variant.Lines, at, other)) ||
                (_touchLength[room, other] > 0 && !SharesWall(rects, otherRects, _touchLength[room, other])) ||
                (_beyondMargin[other, room] >= 0 && !StandsPastAnchor(rects, otherRects, _anchorSides[other], _beyondMargin[other, room])) ||
                (_beyondMargin[room, other] >= 0 && !StandsPastAnchor(otherRects, rects, _anchorSides[room], _beyondMargin[room, other])) ||
                (_keepsClear[other, room] && !ClearsObstacles(rects, _obstacles[other], _placedAt[other])) ||
                (_keepsClear[room, other] && !ClearsObstacles(otherRects, _obstacles[room], at)))
                return false;
        }

        return true;
    }

    private bool KeepsWallGap(int room, ReadOnlySpan<UnitRect> rects, int other)
    {
        bool mayTouch = _mayTouch[room, other];
        int gap = mayTouch ? Math.Max(_problem.MinimumDistance, 2) : _problem.MinimumDistance;
        foreach (var rect in rects)
        {
            foreach (var otherRect in _placedRects[other])
            {
                int distance = GetGap(rect, otherRect);
                if (distance < 0 || (distance < gap && !(mayTouch && distance == 0)))
                    return false;
            }
        }

        return true;
    }

    private bool LinesUpDoor(int room, DoorLine[] lines, Point at, int other)
    {
        foreach (var line in lines)
        {
            if (!MayOpenToward(room, other, line))
                continue;

            var own = line.Offset(at);
            foreach (var placed in _placedLines[other])
            {
                if (placed.Faces(own) && placed.At == own.At && own.Low <= placed.High && placed.Low <= own.High && MayOpenToward(other, room, placed))
                    return true;
            }
        }

        return false;
    }

    private static bool SharesWall(ReadOnlySpan<UnitRect> rects, ReadOnlySpan<UnitRect> otherRects, int length)
    {
        foreach (var a in rects)
        {
            foreach (var b in otherRects)
            {
                if (((a.EndU == b.U || b.EndU == a.U) && Math.Min(a.EndV, b.EndV) - Math.Max(a.V, b.V) >= length) ||
                    ((a.EndV == b.V || b.EndV == a.V) && Math.Min(a.EndU, b.EndU) - Math.Max(a.U, b.U) >= length))
                    return true;
            }
        }

        return false;
    }

    private static bool StandsPastAnchor(ReadOnlySpan<UnitRect> rects, ReadOnlySpan<UnitRect> anchorRects, DoorSides sides, int margin)
    {
        if (sides == DoorSides.None)
            return true;

        foreach (var side in FacingSides)
        {
            if ((sides & side) == 0)
                continue;

            bool isPast = true;
            foreach (var rect in rects)
            {
                foreach (var end in anchorRects)
                {
                    isPast &= side switch
                    {
                        DoorSides.MaxX => rect.U >= end.EndU + margin,
                        DoorSides.MinX => rect.EndU <= end.U - margin,
                        DoorSides.MaxY => rect.V >= end.EndV + margin,
                        _ => rect.EndV <= end.V - margin
                    };
                }
            }

            if (isPast)
                return true;
        }

        return false;
    }

    private bool ClearsObstacles(ReadOnlySpan<UnitRect> rects, UnitRect[] obstacles, Point anchorAt)
    {
        int gap = Math.Max(_problem.MinimumDistance, 1);
        foreach (var rect in rects)
        {
            foreach (var obstacle in obstacles)
            {
                if (GetGap(rect, Offset(obstacle, anchorAt)) < gap)
                    return false;
            }
        }

        return true;
    }

    private void Place(int room, LayoutCandidate candidate)
    {
        var variant = _variants[room][candidate.Variant];
        _placedVariant[room] = candidate.Variant;
        _placedAt[room] = candidate.Position;
        _placedRects[room] = Array.ConvertAll(variant.Rects, rect => Offset(rect, candidate.Position));
        _placedLines[room] = Array.ConvertAll(variant.Lines, line => line.Offset(candidate.Position));
    }

    private static int GetGap(UnitRect a, UnitRect b) => Math.Max(Math.Max(b.U - a.EndU, a.U - b.EndU), Math.Max(b.V - a.EndV, a.V - b.EndV));

    private static UnitRect Offset(UnitRect rect, Point at) => rect with { U = rect.U + at.X, V = rect.V + at.Y };
}

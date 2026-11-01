using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the sections of a surveyed level, split along its tour where no earlier place is visited again.
/// </summary>
public sealed class SectionMeasurer
{
    /// <summary>
    /// Represents the share of the sectioned floor from which a section counts as major.
    /// </summary>
    public const float MajorShare = 0.05f;

    private const int MaxRoomsOfRoom = 3;
    private const float MinHallShare = 0.5f;
    private const int MinComplexLoops = 3;
    private const float MaxSprawlCompactness = 0.45f;

    private readonly LevelSurvey _survey;
    private readonly DoorGraph _graph;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectionMeasurer"/> class.
    /// </summary>
    public SectionMeasurer(LevelSurvey survey)
    {
        _survey = survey;
        _graph = DoorGraph.Create(survey);
    }

    /// <summary>
    /// Returns the sections of the level in tour order, each with the untoured places nearest to its own.
    /// </summary>
    public LevelSection[] Measure()
    {
        var visits = _survey.Route
            .Where(visit => visit.Place >= 1 && visit.Place <= _survey.Places.Count)
            .ToArray();

        var lastVisit = new Dictionary<int, int>();
        for (int visitIndex = 0; visitIndex < visits.Length; visitIndex++)
        {
            lastVisit[visits[visitIndex].Place] = visitIndex;
        }

        var spans = new List<Range>();
        int start = 0;
        int reach = 0;
        for (int visitIndex = 0; visitIndex < visits.Length; visitIndex++)
        {
            reach = Math.Max(reach, lastVisit[visits[visitIndex].Place]);
            if (reach == visitIndex)
            {
                spans.Add(start..(visitIndex + 1));
                start = visitIndex + 1;
            }
        }

        var groups = AttachUntoured(spans.Select(span => visits[span].Select(visit => visit.Place).Distinct().ToList()).ToArray());
        float tourArea = Math.Max(1, groups.Sum(places => places.Sum(place => GetPlace(place).AreaPixels)));

        var blocks = _graph.FindBlocks()
            .Where(block => block.Count >= 3)
            .ToArray();

        var sections = new LevelSection[spans.Count];
        for (int index = 0; index < spans.Count; index++)
        {
            var span = visits[spans[index]];
            var places = groups[index];
            var members = places.ToHashSet();
            int area = places.Sum(place => GetPlace(place).AreaPixels);

            var cells = places
                .SelectMany(place => GetPlace(place).Cells)
                .ToArray();

            int box = (cells.Max(cell => cell.U) - cells.Min(cell => cell.U) + 1) * (cells.Max(cell => cell.V) - cells.Min(cell => cell.V) + 1);
            var section = _graph.GetSubgraph(members);
            var depths = MeasureDepths(section, span);
            int rooms = places.Count(place => GetPlace(place).Kind is not (PlaceKind.SecretCloset or PlaceKind.SpawnCloset or PlaceKind.Nook));

            float hallShare = places
                .Where(place => GetPlace(place).Kind is PlaceKind.Hall or PlaceKind.Yard)
                .Sum(place => GetPlace(place).AreaPixels) / (float)Math
                .Max(1, area);

            int loops = section.CountLoops();
            float compactness = cells.Length / (float)box;
            sections[index] = new LevelSection
            {
                Index = index,
                Places = places,
                Archetype = Classify(rooms, hallShare, loops, compactness),
                Rooms = rooms,
                Entrance = span[0].Place,
                Exit = span[^1].Place,
                Boundary = index == 0 ? null : new SectionBoundary(index, visits[spans[index].Start.Value - 1].Place, span[0].Place,
                    GetBoundaryKind(visits[spans[index].Start.Value - 1].Place, span[0].Place)),
                AreaPixels = area,
                FloorShare = area / tourArea,
                HallShare = hallShare,
                Loops = loops,
                PodDepth = depths.Values.DefaultIfEmpty(0).Max(),
                Depths = depths,
                Compactness = compactness,
                TourStart = span[0].Enter,
                TourEnd = span[^1].Leave,
                SharedBlocks = blocks.Count(block => block.Overlaps(members) && !block.IsSubsetOf(members))
            };
        }

        return sections;
    }

    private static SectionArchetype Classify(int rooms, float hallShare, int loops, float compactness)
    {
        if (hallShare >= MinHallShare)
        {
            return SectionArchetype.Hall;
        }

        if (rooms <= MaxRoomsOfRoom)
        {
            return SectionArchetype.Room;
        }

        if (loops >= MinComplexLoops)
        {
            return SectionArchetype.Complex;
        }

        return compactness < MaxSprawlCompactness ? SectionArchetype.Sprawl : SectionArchetype.Wing;
    }

    private LevelPlace GetPlace(int place) => _survey.Places[place - 1];

    private int[][] AttachUntoured(List<int>[] groups)
    {
        var sectionOf = new Dictionary<int, int>();
        var queue = new Queue<int>();
        for (int index = 0; index < groups.Length; index++)
        {
            foreach (int place in groups[index])
            {
                sectionOf[place] = index;
                queue.Enqueue(place);
            }
        }

        var neighbors = _survey.Connections
            .Where(connection => connection.A != connection.B && connection.A >= 1 && connection.B >= 1)
            .SelectMany(connection => new[] { (From: connection.A, To: connection.B), (From: connection.B, To: connection.A) })
            .ToLookup(pair => pair.From, pair => pair.To);

        while (queue.Count > 0)
        {
            int place = queue.Dequeue();
            foreach (int next in neighbors[place].Order())
            {
                if (sectionOf.TryAdd(next, sectionOf[place]))
                {
                    groups[sectionOf[place]].Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return groups.Select(group => group.ToArray()).ToArray();
    }

    private BoundaryKind GetBoundaryKind(int from, int to)
    {
        var kinds = _survey.Connections
            .Where(connection => (connection.A == from && connection.B == to) || (connection.A == to && connection.B == from))
            .Select(connection => connection.Kind)
            .ToHashSet();

        if (kinds.Contains(ConnectionKind.LockedDoor))
        {
            return BoundaryKind.LockedDoor;
        }

        if (kinds.Contains(ConnectionKind.Fence))
        {
            return BoundaryKind.Fence;
        }

        if (kinds.Contains(ConnectionKind.BrokenWall) || kinds.Contains(ConnectionKind.SecretWall))
        {
            return BoundaryKind.Breach;
        }

        var earlier = GetPlace(from);
        var later = GetPlace(to);
        if (later.Zone != earlier.Zone)
        {
            switch (later.Zone)
            {
                case GateZone.Locked:
                    return BoundaryKind.LockedDoor;
                case GateZone.Blackout:
                    return BoundaryKind.Blackout;
                case GateZone.Fenced:
                    return BoundaryKind.Fence;
            }
        }

        return earlier.Kind == PlaceKind.Corridor || later.Kind == PlaceKind.Corridor ? BoundaryKind.Corridor : BoundaryKind.Passage;
    }

    private static Dictionary<int, int> MeasureDepths(DoorGraph section, RouteVisit[] span)
    {
        var rooms = span
            .Select(visit => visit.Place)
            .Where(section.Neighbors.ContainsKey)
            .ToArray();

        if (rooms.Length == 0)
        {
            return [];
        }

        var depth = section.FindMainBody(rooms[0], rooms[^1]).ToDictionary(room => room, _ => 0);
        var queue = new Queue<int>(depth.Keys);
        while (queue.Count > 0)
        {
            int room = queue.Dequeue();
            foreach (int next in section.Neighbors[room])
            {
                if (depth.TryAdd(next, depth[room] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return depth;
    }
}

using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Design;

/// <summary>
/// Represents the door graph of a level's rooms and halls: its main body from the start to the end of the tour, the branches off
/// it, its hub and how the tour crosses its sections.
/// </summary>
public sealed class RoomGraph
{
    private const int SmallRoomPixels = 70000;
    private const float SquareStretch = 1.15f;
    private const int MidRoomPixels = 100000;
    private const int LargeRoomPixels = 400000;
    private const int MinNotchSide = 2;
    private const int BareFurniture = 3;
    private const float MinNotchShare = 0.04f;

    private static readonly LandmarkKind[] Objectives = [LandmarkKind.Breaker, LandmarkKind.DynamiteCharges, LandmarkKind.DynamiteSite, LandmarkKind.Boss, LandmarkKind.Generator];
    
    /// <summary>
    /// Gets the number of rooms and halls.
    /// </summary>
    public int Rooms { get; private init; }

    /// <summary>
    /// Gets the number of rooms and halls under 70,000 square pixels.
    /// </summary>
    public int SmallRooms { get; private init; }

    /// <summary>
    /// Gets the number of independent cycles of the door graph.
    /// </summary>
    public int Cycles { get; private init; }

    /// <summary>
    /// Gets the share of the floor in rooms on some path from the start to the end of the tour.
    /// </summary>
    public float MainBodyShare { get; private init; }

    /// <summary>
    /// Gets the number of doors of the room with the most doors.
    /// </summary>
    public int HubDoors { get; private init; }

    /// <summary>
    /// Gets the number of rooms walked from the start room to the hub.
    /// </summary>
    public int HubHops { get; private init; }

    /// <summary>
    /// Gets the number of branches of two or more rooms.
    /// </summary>
    public int LargeBranches { get; private init; }

    /// <summary>
    /// Gets the number of branches of two or more rooms that open off the hub.
    /// </summary>
    public int LargeBranchesOnHub { get; private init; }

    /// <summary>
    /// Gets the floor share of the largest branch.
    /// </summary>
    public float LargestBranchShare { get; private init; }

    /// <summary>
    /// Gets the number of rooms of the largest branch.
    /// </summary>
    public int LargestBranchRooms { get; private init; }

    /// <summary>
    /// Gets the number of rooms in branches that hold no objective and no fight.
    /// </summary>
    public int LootRooms { get; private init; }

    /// <summary>
    /// Gets the number of rooms between each objective's room and the main body, or -1 for an objective outside the graph.
    /// </summary>
    public IReadOnlyList<int> ObjectiveDepths { get; private init; } = [];

    /// <summary>
    /// Gets the share of rooms whose length is within 15% of their width.
    /// </summary>
    public float SquareShare { get; private init; }

    /// <summary>
    /// Gets the share of rooms under 100,000 square pixels with no notch, or null without such rooms.
    /// </summary>
    /// <remarks>A notch is a part of a room's bounding box outside the room at least 2 cells across and 4% of the box.</remarks>
    public float? SmallRectangleShare { get; private init; }

    /// <summary>
    /// Gets the share of rooms from 100,000 to 400,000 square pixels with no notch, or null without such rooms.
    /// </summary>
    public float? MidRectangleShare { get; private init; }

    /// <summary>
    /// Gets the share of rooms of 400,000 square pixels or more with no notch, or null without such rooms.
    /// </summary>
    public float? LargeRectangleShare { get; private init; }

    /// <summary>
    /// Gets the number of room notches.
    /// </summary>
    public int Notches { get; private init; }

    /// <summary>
    /// Gets the share of room notches that open outside the building, or null without notches.
    /// </summary>
    public float? OutsideNotchShare { get; private init; }

    /// <summary>
    /// Gets the number of rooms of the first section the tour walks again after it enters the second, or null without a gate key.
    /// </summary>
    public int? SectionReentries { get; private init; }

    /// <summary>
    /// Gets the share of the tour after the second section starts that it spends in rooms of the first, or null without a gate key.
    /// </summary>
    public float? SectionReentryShare { get; private init; }

    /// <summary>
    /// Gets the share of the ambush hit points that spawn into rooms the tour walked before the trigger and never walks again.
    /// </summary>
    public float AbandonedAmbushShare { get; private init; }

    /// <summary>
    /// Gets the number of rooms other than halls and closets that hold no decor.
    /// </summary>
    public int BareRooms { get; private init; }

    /// <summary>
    /// Builds the room graph of a surveyed level.
    /// </summary>
    public static RoomGraph Build(LevelSurvey survey)
    {
        var places = survey.Places.ToDictionary(place => place.Id);
        var graph = DoorGraph.Create(survey);
        var rooms = graph.Rooms;
        var neighbors = graph.Neighbors;
        (int start, int end) = DoorGraph.FindEnds(survey);
        var body = graph.FindMainBody(start, end);
        var depth = neighbors.MeasureHops(body);
        float floor = Math.Max(1, rooms.Sum(room => (float)places[room].AreaPixels));
        int edges = neighbors.Values.Sum(set => set.Count) / 2;
        int components = graph.CountComponents();

        var objectivePlaces = survey.Landmarks.Where(landmark => Objectives.Contains(landmark.Kind)).Select(landmark => landmark.Place).ToHashSet();
        var fightPlaces = survey.Encounters.Select(encounter => encounter.Place).ToHashSet();
        var branches = graph.FindBranches(body);
        int hub = rooms.MaxBy(room => (neighbors[room].Count, -depth.GetValueOrDefault(room, int.MaxValue / 2)))!;
        var large = branches.Where(branch => branch.Count >= 2).ToArray();
        float[] stretches = rooms.Select(room => places[room]).Select(place => Math.Max(place.LengthPixels, place.WidthPixels) / (float)Math.Max(1, Math.Min(place.LengthPixels, place.WidthPixels))).ToArray();
        var notches = MeasureNotches(survey);
        int allNotches = notches.Values.Sum(notch => notch.Count);
        (int? reentries, float? reentryShare) = MeasureSectionReentry(survey);
        return new RoomGraph
        {
            Rooms = rooms.Count,
            SmallRooms = rooms.Count(room => places[room].AreaPixels < SmallRoomPixels),
            Cycles = edges - rooms.Count + components,
            MainBodyShare = body.Sum(room => places[room].AreaPixels) / floor,
            HubDoors = neighbors[hub].Count,
            HubHops = neighbors.MeasureHops([start]).GetValueOrDefault(hub, -1),
            LargeBranches = large.Length,
            LargeBranchesOnHub = large.Count(branch => branch.Any(room => neighbors[room].Contains(hub))),
            LargestBranchShare = branches.Select(branch => branch.Sum(room => places[room].AreaPixels) / floor).DefaultIfEmpty(0).Max(),
            LargestBranchRooms = branches.Select(branch => branch.Count).DefaultIfEmpty(0).Max(),
            LootRooms = branches.Where(branch => !branch.Any(objectivePlaces.Contains) && !branch.Any(fightPlaces.Contains)).Sum(branch => branch.Count),
            ObjectiveDepths = survey.Landmarks.Where(landmark => Objectives.Contains(landmark.Kind)).Select(landmark => depth.GetValueOrDefault(landmark.Place, -1)).Order().ToArray(),
            
            SquareShare = stretches.Count(stretch => stretch < SquareStretch) / (float)Math.Max(1, rooms.Count),
            SmallRectangleShare = GetRectangleShare(survey, notches, 0, MidRoomPixels),
            MidRectangleShare = GetRectangleShare(survey, notches, MidRoomPixels, LargeRoomPixels),
            LargeRectangleShare = GetRectangleShare(survey, notches, LargeRoomPixels, int.MaxValue),
            Notches = allNotches,
            OutsideNotchShare = allNotches == 0 ? null : notches.Values.Sum(notch => notch.Outside) / (float)allNotches,
            SectionReentries = reentries,
            SectionReentryShare = reentryShare,
            AbandonedAmbushShare = MeasureAbandonedAmbushes(survey),
            BareRooms = rooms.Count(room => places[room].Kind == PlaceKind.Room && places[room].FurniturePieces < BareFurniture)
        };
    }

    private static float? GetRectangleShare(LevelSurvey survey, Dictionary<int, (int Count, int Outside)> notches, int minPixels, int maxPixels)
    {
        var band = survey.Places.Where(place => place.Kind == PlaceKind.Room && place.AreaPixels >= minPixels && place.AreaPixels < maxPixels).ToArray();
        return band.Length == 0 ? null : band.Count(place => notches[place.Id].Count == 0) / (float)band.Length;
    }

    // Count the parts of each room's bounding box outside it that are wider than a wall sliver, and those that open outside the building
    private static Dictionary<int, (int Count, int Outside)> MeasureNotches(LevelSurvey survey)
    {
        var inside = survey.Places.SelectMany(place => place.Cells).ToHashSet();
        var outside = FindOutside(inside);
        var result = new Dictionary<int, (int Count, int Outside)>();
        foreach (var place in survey.Places.Where(place => place.Kind == PlaceKind.Room))
        {
            var cells = place.Cells.ToHashSet();
            int minU = cells.Min(cell => cell.U), maxU = cells.Max(cell => cell.U), minV = cells.Min(cell => cell.V), maxV = cells.Max(cell => cell.V);
            var missing = new HashSet<Cell>();
            for (int u = minU; u <= maxU; u++)
            {
                for (int v = minV; v <= maxV; v++)
                {
                    if (!cells.Contains(new Cell(u, v)))
                    {
                        missing.Add(new Cell(u, v));
                    }
                }
            }

            int box = (maxU - minU + 1) * (maxV - minV + 1);
            int count = 0, open = 0;
            foreach (var notch in FindCellComponents(missing))
            {
                int width = notch.Max(cell => cell.U) - notch.Min(cell => cell.U) + 1;
                int height = notch.Max(cell => cell.V) - notch.Min(cell => cell.V) + 1;
                if (width < MinNotchSide || height < MinNotchSide || notch.Count < MinNotchShare * box)
                {
                    continue;
                }

                count++;
                if (2 * notch.Count(outside.Contains) > notch.Count)
                {
                    open++;
                }
            }

            result[place.Id] = (count, open);
        }

        return result;
    }

    // Flood the cells no place holds from beyond the edge of the level
    private static HashSet<Cell> FindOutside(HashSet<Cell> inside)
    {
        int minU = inside.Min(cell => cell.U) - 1, maxU = inside.Max(cell => cell.U) + 1, minV = inside.Min(cell => cell.V) - 1, maxV = inside.Max(cell => cell.V) + 1;
        var first = new Cell(minU, minV);
        var outside = new HashSet<Cell> { first };
        var queue = new Queue<Cell>([first]);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var next in GetSides(cell))
            {
                if (next.U >= minU && next.U <= maxU && next.V >= minV && next.V <= maxV && !inside.Contains(next) && outside.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return outside;
    }

    private static List<HashSet<Cell>> FindCellComponents(HashSet<Cell> cells)
    {
        var left = new HashSet<Cell>(cells);
        var components = new List<HashSet<Cell>>();
        while (left.Count > 0)
        {
            var component = left.Flood([left.First()], _ => false);
            left.ExceptWith(component);
            components.Add(component);
        }

        return components;
    }

    private static Cell[] GetSides(Cell cell) => [cell with { U = cell.U + 1 }, cell with { U = cell.U - 1 }, cell with { V = cell.V + 1 }, cell with { V = cell.V - 1 }];

    // Count the rooms of the first section the tour walks after the second starts, at the first locked-door visit of a blackout or
    // the first new room after the key
    private static (int? Rooms, float? Share) MeasureSectionReentry(LevelSurvey survey)
    {
        var route = survey.Route;
        int? key = survey.Gates.FirstOrDefault(gate => gate.KeyPlace is not null)?.KeyPlace;
        if (key is null || route.Count == 0)
        {
            return (null, null);
        }

        int boundary;
        if (route[0].Phase == RoutePhase.Free && route.Any(visit => visit.Phase == RoutePhase.DoorsLocked))
        {
            boundary = route.ToList().FindIndex(visit => visit.Phase == RoutePhase.DoorsLocked);
        }
        else
        {
            int last = route.ToList().FindLastIndex(visit => visit.Place == key);
            boundary = last < 0 ? route.Count : Enumerable.Range(last + 1, route.Count - last - 1)
                .FirstOrDefault(index => route.Take(index).All(visit => visit.Place != route[index].Place), route.Count);
        }

        if (boundary >= route.Count)
        {
            return (0, 0);
        }

        var before = route.Take(boundary).Select(visit => visit.Place).ToHashSet();
        var back = route.Skip(boundary).Where(visit => before.Contains(visit.Place)).ToArray();
        int rest = Math.Max(1, route[^1].Leave - route[boundary].Enter);
        return (back.Select(visit => visit.Place).Distinct().Count(), back.Sum(visit => visit.Leave - visit.Enter) / (float)rest);
    }

    private static float MeasureAbandonedAmbushes(LevelSurvey survey)
    {
        var closets = survey.Places.Where(place => place.Kind is PlaceKind.SecretCloset or PlaceKind.SpawnCloset).Select(place => place.Id).ToHashSet();
        var openings = new Dictionary<int, int>();
        foreach (var connection in survey.Connections)
        {
            if (closets.Contains(connection.A))
            {
                openings[connection.A] = connection.B;
            }

            if (closets.Contains(connection.B))
            {
                openings[connection.B] = connection.A;
            }
        }

        var ambushes = survey.Encounters.Where(encounter => encounter.Kind == EncounterKind.Ambush).ToArray();
        int total = ambushes.Sum(ambush => ambush.HitPoints);
        int abandoned = ambushes
            .Where(ambush =>
            {
                int room = openings.GetValueOrDefault(ambush.Place, ambush.Place);
                return survey.Route.Any(visit => visit.Place == room && visit.Leave < ambush.Route) && !survey.Route.Any(visit => visit.Place == room && visit.Enter > ambush.Route);
            })
            .Sum(ambush => ambush.HitPoints);
        return abandoned / (float)Math.Max(1, total);
    }
}

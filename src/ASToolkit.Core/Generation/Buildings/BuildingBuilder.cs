using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the building of a level, laid out by its building's grammar from the level's program and written in layout units.
/// </summary>
public sealed class BuildingBuilder
{
    private const int LayoutUnitsPerGridUnit = 1;

    private readonly Random _rng;
    private readonly IReadOnlyList<RoomFootprint> _footprints;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildingBuilder"/> class.
    /// </summary>
    public BuildingBuilder(Random rng, IReadOnlyList<RoomFootprint> footprints)
    {
        _rng = rng;
        _footprints = footprints;
    }

    /// <summary>
    /// Gets the fallbacks and built parts the last building counted.
    /// </summary>
    public IReadOnlyDictionary<string, int> Counts { get; private set; } = new Dictionary<string, int>();

    /// <summary>
    /// Builds the building a level asks for.
    /// </summary>
    public BuildingPlan Build(BuildingRequest request)
    {
        BuildingGrammar grammar = request.Building switch
        {
            Building.Warehouse when request.Style == GenerationStyle.Campaign => new WarehouseGrammar(request, _rng),
            Building.OfficeBlock when request.Gate == LevelStyle.Blackout => new OfficeGrammar(request, _rng),
            // Lay the add-on buildings and add-on warehouses as labs until their templates exist
            Building.UndergroundLab or Building.Warehouse or Building.Prison or Building.OfficeBlock or Building.ConferenceBuilding or Building.School
                or Building.TrainingComplex => new LabGrammar(request, _rng),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Building, "Expected an indoor building.")
        };

        grammar.Build();
        if (grammar.Grid.Rooms.Count == 0)
        {
            // Lay a building its own grammar left unlaid as a lab
            grammar = new LabGrammar(request, _rng);
            grammar.Count(BuildingTally.BuildingUnlaid);
            grammar.Build();
        }

        grammar.TopUp();
        if (request.Building == Building.UndergroundLab)
        {
            new RoomShaper(grammar.Draft).ShapeRooms(_footprints);
        }

        grammar.PlaceSideRooms();
        Counts = grammar.Counts;
        return Write(grammar, request);
    }

    private BuildingPlan Write(BuildingGrammar grammar, BuildingRequest request)
    {
        var grid = grammar.Grid;
        var idOf = new Dictionary<int, int>();
        var rooms = new List<BuildingRoom>();
        var extras = grid.Rooms
            .Where(room => room.Kind is GridKind.Neck or GridKind.Arm && room.Owner is not null)
            .ToLookup(room => room.Owner!.Value, room => room.Rect);

        var necks = grid.Rooms
            .Where(room => room is { Kind: GridKind.Neck, Owner: not null })
            .ToLookup(room => room.Owner!.Value, room => room.Rect);

        foreach (var room in grid.Rooms.Where(room => room.Kind is not (GridKind.Neck or GridKind.Arm or GridKind.Court)))
        {
            idOf[room.Id] = rooms.Count;
            var kind = room.Kind switch
            {
                GridKind.Hall => BuildingRoomKind.Hall,
                GridKind.SecretCloset => BuildingRoomKind.SecretCloset,
                GridKind.SecretRoom => BuildingRoomKind.SecretRoom,
                GridKind.SpawnCloset => BuildingRoomKind.SpawnCloset,
                _ => BuildingRoomKind.Room
            };

            rooms.Add(new BuildingRoom
            {
                Id = rooms.Count,
                Kind = kind,
                Type = kind == BuildingRoomKind.Hall ? RoomType.Hall : room.Type ?? RoomType.Office,
                Rects = extras[room.Id].Prepend(room.Rect).Select(Scale).ToArray(),
                Necks = necks[room.Id].Select(Scale).ToArray(),
                Phase = room.Phase,
                Host = room.Host is null ? null : idOf.GetValueOrDefault(room.Host.Value)
            });
        }

        var jammed = grammar.JammedWalls
            .Where(pair => idOf.ContainsKey(pair.A) && idOf.ContainsKey(pair.B))
            .Select(pair => new RoomPair(idOf[pair.A], idOf[pair.B]))
            .ToArray();

        var shortcuts = grammar.Shortcuts
            .Where(pair => idOf.ContainsKey(pair.From) && idOf.ContainsKey(pair.To))
            .Select(pair => (idOf[pair.From], idOf[pair.To]))
            .ToArray();

        // Never shut a wall that holds a designed door, lock or shortcut
        var designed = grammar.Junctions
            .Where(pair => idOf.ContainsKey(pair.Room) && idOf.ContainsKey(pair.Passage))
            .Select(pair => (idOf[pair.Room], idOf[pair.Passage]))
            .Concat(grammar.Locks.Where(pair => idOf.ContainsKey(pair.From) && idOf.ContainsKey(pair.To)).Select(pair => (idOf[pair.From], idOf[pair.To])))
            .Concat(shortcuts)
            .Select(pair => new RoomPair(pair.Item1, pair.Item2))
            .ToHashSet();

        var closedWalls = grammar.ClosedWalls
            .Where(pair => idOf.ContainsKey(pair.A) && idOf.ContainsKey(pair.B))
            .Select(pair => new RoomPair(idOf[pair.A], idOf[pair.B]))
            .Where(pair => !designed.Contains(pair))
            .ToArray();

        return new BuildingPlan
        {
            Gate = request.Gate,
            HasSites = request.HasSites,
            Rooms = rooms,
            Contacts = FindContacts(rooms),
            Start = grammar.Start is null ? rooms.FindIndex(room => room.Kind == BuildingRoomKind.Room) : Map(grammar.Start.Value),
            Keys = grammar.Keys.Select(Map).ToArray(),
            GateRoom = grammar.GateRoom is null ? null : Map(grammar.GateRoom.Value),
            Trigger = grammar.Trigger is null ? null : Map(grammar.Trigger.Value),
            End = grammar.End is null ? null : Map(grammar.End.Value),
            Locks = grammar.Locks.Select(pair => (Map(pair.From), Map(pair.To))).ToArray(),
            Shortcuts = shortcuts,
            ClosedWalls = closedWalls,
            JammedWalls = jammed,
            OpenWalls = grammar.OpenWalls
                .Where(pair => idOf.ContainsKey(pair.A) && idOf.ContainsKey(pair.B))
                .Select(pair => new RoomPair(idOf[pair.A], idOf[pair.B]))
                .ToArray(),
            SecondWing = grammar.SecondWing.Where(idOf.ContainsKey).Select(Map).ToArray(),
            LockedSiteRooms = grammar.LockedSiteRooms.Where(idOf.ContainsKey).Select(Map).ToArray()
        };

        int Map(int id) => idOf[id];
    }

    private static UnitRect Scale(UnitRect rect) =>
        new(rect.U * LayoutUnitsPerGridUnit, rect.V * LayoutUnitsPerGridUnit, rect.W * LayoutUnitsPerGridUnit, rect.H * LayoutUnitsPerGridUnit);

    // Returns the halls and rooms that share at least one grid unit of wall
    private static RoomPair[] FindContacts(IReadOnlyList<BuildingRoom> rooms)
    {
        var walkable = rooms.Where(room => room.Kind is BuildingRoomKind.Hall or BuildingRoomKind.Room).ToArray();
        var contacts = new List<RoomPair>();
        for (int first = 0; first < walkable.Length; first++)
        {
            for (int second = first + 1; second < walkable.Length; second++)
            {
                int shared = walkable[first].Rects.Sum(a => walkable[second].Rects.Sum(b => a.GetSharedEdges(b).Length));
                if (shared >= LayoutUnitsPerGridUnit)
                {
                    contacts.Add(new RoomPair(walkable[first].Id, walkable[second].Id));
                }
            }
        }

        return contacts.ToArray();
    }
}

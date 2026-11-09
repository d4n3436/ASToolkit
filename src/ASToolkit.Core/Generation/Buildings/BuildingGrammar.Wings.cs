using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

internal abstract partial class BuildingGrammar
{
    // Holds a length in room sides or units, plus an addition, times a drawn count, within its bounds
    private readonly record struct WingExtent(float Min, float Max, int Add = 0, int MinLength = 0, int MaxLength = int.MaxValue, bool IsUnits = false, string? Times = null);

    private enum WingShapeKind
    {
        Box,
        Cross,
        Notched,
        BigRoom
    }

    private readonly record struct WingShape(WingShapeKind Kind, WingExtent Width, WingExtent Length);

    // Holds a count drawn as a share of the wing's rooms or as the rest the fixed rooms and earlier counts leave, within its bounds, with a chance
    // and from a room count on
    private sealed record WingCount(string Name, float Share = 0, int Offset = 0, int Min = 0, int Max = int.MaxValue, bool IsRest = false, float Chance = 1,
        int MinRooms = 0);

    private enum WingVia
    {
        Door,
        Neck,
        Passage,
        NeckOrDoor,
        PassageOrNeck
    }

    private enum WingSides
    {
        Any,
        Far,
        Near,
        Down,
        Across,
        HostEnds,
        RoomEnds
    }

    // Holds a room of a wing, laid once or as many times as its count; its first instances solve with the wing and the rest are laid one by one
    // beside their hosts. Hosts and sides cycle by instance and the via list's last entry repeats. "previous" names the instance before (the hall
    // for the first), "name#" the instance of the same index and "name^" the last instance (the wing's first room when there is none)
    private sealed record WingNode(string Name, RoomType Type, IReadOnlyList<IReadOnlyList<WingShape>> Variants)
    {
        public bool DrawsBackType { get; init; }

        public IReadOnlyList<(RoomType Type, float Weight)> Types { get; init; } = [];

        public bool IsPiece { get; init; }

        public bool IsHall { get; init; }

        public string? Count { get; init; }

        public int CountShift { get; init; }

        public int InSolve { get; init; } = int.MaxValue;

        public IReadOnlyList<string> Hosts { get; init; } = [];

        public IReadOnlyList<WingVia> Via { get; init; } = [WingVia.Door];

        public JoinMark Mark { get; init; } = JoinMark.Junction;

        public IReadOnlyList<WingSides> Sides { get; init; } = [WingSides.Any];

        public IReadOnlyList<string> Packs { get; init; } = [];

        public IReadOnlyList<string> Beside { get; init; } = [];
    }

    private readonly record struct WingJoin(string From, string To, WingVia Via, JoinMark Mark = JoinMark.Junction);

    // Holds a wing's hall, entered off the joint at its end, crossed on a long side or standing perpendicular past the joint; a wing without one
    // joins its rooms to the joint
    private readonly record struct WingHall(WingExtent Length, bool IsBeyond = false, int Width = 0);

    [Flags]
    private enum WingStep
    {
        None = 0,
        LinePods = 1,
        LineBigRooms = 2
    }

    private sealed record WingTemplate(LabWing Kind, int MinRooms, int MaxRooms, WingHall? Hall, IReadOnlyList<WingNode> Nodes)
    {
        public IReadOnlyList<WingCount> Counts { get; init; } = [];

        public IReadOnlyList<WingJoin> Joins { get; init; } = [];

        public IReadOnlyList<(string A, string B)> Touches { get; init; } = [];

        public IReadOnlyList<(string A, string B)> Contacts { get; init; } = [];

        public int Draws { get; init; } = 1;

        public required string End { get; init; }

        public WingStep Steps { get; init; }
    }

    private static WingShape Box(float min, float max, float minLength, float maxLength, int least) =>
        new(WingShapeKind.Box, new WingExtent(min, max, MinLength: least), new WingExtent(minLength, maxLength, MinLength: least));

    private static WingShape UnitBox(int width, int length) =>
        new(WingShapeKind.Box, new WingExtent(width, width, IsUnits: true), new WingExtent(length, length, IsUnits: true));

    private static readonly IReadOnlyList<WingShape> SmallPassage = [UnitBox(3, 4), UnitBox(3, 5), UnitBox(4, 3), UnitBox(5, 3)];

    private static readonly IReadOnlyList<WingShape> AirlockLeg =
    [
        new(WingShapeKind.Box, new WingExtent(3, 3, IsUnits: true), new WingExtent(1.6f, 2.2f)),
        new(WingShapeKind.Box, new WingExtent(1.6f, 2.2f), new WingExtent(3, 3, IsUnits: true)),
        new(WingShapeKind.Box, new WingExtent(3, 3, IsUnits: true), new WingExtent(2.0f, 2.6f)),
        new(WingShapeKind.Box, new WingExtent(2.0f, 2.6f), new WingExtent(3, 3, IsUnits: true))
    ];

    private static readonly WingTemplate Gallery = new(LabWing.Gallery, 7, 9, new WingHall(new WingExtent(1, 1, Add: 2, Times: "cells")),
    [
        new WingNode("service", RoomType.Passage, [[new WingShape(WingShapeKind.Box, new WingExtent(3, 3, IsUnits: true), new WingExtent(1, 1, Add: 1, Times: "cells"))]]),
        new WingNode("passageA", RoomType.Passage, [SmallPassage]),
        new WingNode("passageB", RoomType.Passage, [SmallPassage]),
        new WingNode("control", RoomType.Lab, [[Box(1.3f, 1.6f, 1.0f, 1.25f, 6)]]) { IsPiece = true, Hosts = ["service"], Sides = [WingSides.Far] },
        new WingNode("widest", RoomType.Storage, [[Box(1.2f, 1.45f, 0.85f, 1.0f, 5)]]) { DrawsBackType = true, IsPiece = true, Hosts = ["service"], Sides = [WingSides.Near] },
        new WingNode("cell", RoomType.Storage, [[Box(0.85f, 1.0f, 0.85f, 1.0f, 5)]]) { DrawsBackType = true, Count = "cells", CountShift = -1, InSolve = 0, Hosts = ["service"], Beside = ["control"] },
        new WingNode("pocket", RoomType.Storage, [[Box(0.6f, 0.8f, 0.6f, 0.8f, 4)]]) { Count = "pocket", InSolve = 0, Hosts = ["passageA"], Beside = ["passageB"] }
    ])
    {
        Counts = [new WingCount("pocket", Offset: -1, Max: 1, Chance: 0.5f, MinRooms: 6), new WingCount("cells", Offset: -1, IsRest: true, Min: 3, Max: 6)],
        Joins = [new WingJoin("hall", "passageA", WingVia.Door, JoinMark.Contact), new WingJoin("passageA", "service", WingVia.Door, JoinMark.Contact),
            new WingJoin("hall", "passageB", WingVia.Door, JoinMark.Contact), new WingJoin("passageB", "service", WingVia.Door, JoinMark.Contact)],
        End = "control",
        Steps = WingStep.LinePods
    };

    private static readonly WingTemplate Echelon = new(LabWing.Echelon, 6, 12, new WingHall(new WingExtent(2.0f, 3.0f)),
    [
        new WingNode("lab", RoomType.Lab, [[Box(1.0f, 1.2f, 0.9f, 1.05f, 6)], [Box(0.8f, 0.9f, 0.75f, 0.85f, 6)]])
        {
            IsPiece = true, Count = "labs", Hosts = ["previous"], Via = [WingVia.Door, WingVia.PassageOrNeck], Sides = [WingSides.Down, WingSides.Across]
        },
        new WingNode("nook", RoomType.Storage, [[Box(0.6f, 0.7f, 0.55f, 0.7f, 4)]]) { Count = "nooks", InSolve = 0, Hosts = ["lab#"] }
    ])
    {
        Counts = [new WingCount("labs", Share: 0.7f, Min: 3, Max: 5), new WingCount("nooks", IsRest: true, Max: 4)],
        End = "lab",
        Steps = WingStep.LinePods
    };

    private static readonly WingTemplate Airlock = new(LabWing.Airlock, 8, 11, new WingHall(new WingExtent(2.2f, 3.0f)),
    [
        new WingNode("core", RoomType.Lab, [[Box(1.45f, 1.7f, 1.3f, 1.55f, 8)]]) { IsPiece = true },
        new WingNode("legA", RoomType.Passage, [AirlockLeg]),
        new WingNode("legB", RoomType.Passage, [AirlockLeg]),
        new WingNode("pod", RoomType.Lab, [[Box(0.75f, 0.95f, 0.65f, 0.85f, 5)]]) { DrawsBackType = true, Count = "pods", InSolve = 2, Hosts = ["legA", "legB"], Beside = ["legA", "legB", "core"] }
    ])
    {
        Counts = [new WingCount("pods", IsRest: true, Min: 2, Max: 6)],
        Joins = [new WingJoin("hall", "legA", WingVia.Door), new WingJoin("legA", "core", WingVia.Passage), new WingJoin("core", "legB", WingVia.Passage), new WingJoin("hall", "legB", WingVia.Door)],
        End = "core",
        Steps = WingStep.LinePods
    };

    private static readonly WingTemplate Suite = new(LabWing.Suite, 3, 6, null,
    [
        new WingNode("office", RoomType.Office, [[new WingShape(WingShapeKind.Notched, new WingExtent(1.8f, 2.3f, MinLength: 5), new WingExtent(1.6f, 2.0f, MinLength: 5))]])
        {
            Types = [(RoomType.Office, 0.6f), (RoomType.Lab, 0.4f)], IsPiece = true, Hosts = ["hall"]
        },
        new WingNode("side", RoomType.Storage, [[Box(1.0f, 1.15f, 0.85f, 1.4f, 5)]])
        {
            Types = [(RoomType.Storage, 0.45f), (RoomType.Office, 0.35f)], IsPiece = true, Count = "sides", Hosts = ["office"], Via = [WingVia.NeckOrDoor]
        },
        new WingNode("depot", RoomType.Depot, [[Box(0.7f, 0.85f, 0.6f, 1.05f, 5)], [new WingShape(WingShapeKind.Notched, new WingExtent(0.7f, 0.85f, MinLength: 5), new WingExtent(0.6f, 1.05f, MinLength: 5))]])
        {
            IsPiece = true, Count = "depot", Hosts = ["side^"], Via = [WingVia.NeckOrDoor]
        }
    ])
    {
        Counts = [new WingCount("depot", Share: 1, Max: 1, MinRooms: 3), new WingCount("sides", IsRest: true, Min: 1)],
        Contacts = [("side", "side^")],
        Draws = 3,
        End = "side"
    };

    private static readonly WingShape BigRoom = new(WingShapeKind.BigRoom, default, default);

    private static readonly WingShape Cross = new(WingShapeKind.Cross, default, default);

    private static readonly WingShape HallRun = new(WingShapeKind.Box, new WingExtent(8, 8, IsUnits: true), new WingExtent(8, 10, MinLength: 30, MaxLength: 60, IsUnits: true, Times: "perHall"));

    private static readonly WingTemplate BigRoomHalls = new(LabWing.BigRoomHalls, 8, 10, new WingHall(HallRun.Length, Width: 8),
    [
        new WingNode("crossA", RoomType.Passage, [[Cross]]) { Count = "second", Hosts = ["hall"], Via = [WingVia.Neck], Sides = [WingSides.HostEnds] },
        new WingNode("roomA", RoomType.ComputerRoom, [[BigRoom]]) { IsPiece = true, Count = "second", Hosts = ["crossA"], Sides = [WingSides.HostEnds] },
        new WingNode("hallB", RoomType.Passage, [[HallRun]]) { IsHall = true, Count = "second", Hosts = ["crossA"], Via = [WingVia.Neck], Sides = [WingSides.RoomEnds] },
        new WingNode("crossB", RoomType.Passage, [[Cross]]) { Count = "third", Hosts = ["hallB"], Via = [WingVia.Neck], Sides = [WingSides.HostEnds] },
        new WingNode("roomB", RoomType.ComputerRoom, [[BigRoom]]) { IsPiece = true, Count = "third", Hosts = ["crossB"], Sides = [WingSides.HostEnds] },
        new WingNode("hallC", RoomType.Passage, [[HallRun]]) { IsHall = true, Count = "third", Hosts = ["crossB"], Via = [WingVia.Neck], Sides = [WingSides.RoomEnds] }
    ])
    {
        Counts =
        [
            new WingCount("second", Share: 1, Max: 1, MinRooms: 5), new WingCount("third", Share: 1, Max: 1, MinRooms: 8),
            new WingCount("perHall", Share: 1.0f / 3, Min: 2)
        ],
        End = "hall",
        Steps = WingStep.LineBigRooms
    };

    private static readonly WingTemplate[] SolvedWings = [Gallery, Echelon, Airlock, Suite, BigRoomHalls];

    private List<int> LayWing(WingTemplate template, int joint, int rooms, int phase)
    {
        for (int draw = 0; draw < template.Draws; draw++)
        {
            var built = TryLayWing(template, joint, rooms, phase);
            if (built.Count > 0)
            {
                return built;
            }
        }

        return [];
    }

    private List<int> TryLayWing(WingTemplate template, int joint, int rooms, int phase)
    {
        var counts = DrawCounts(template, rooms);
        var wing = new GraphWing(this, template.Hall?.IsBeyond ?? true);
        bool isRight = Random.Next() < 0.5f;
        int hall = template.Hall is null ? WingSolver.Anchor : AddTemplateHall(wing, template.Hall.Value, counts, phase);
        var ids = new Dictionary<string, List<int>> { ["hall"] = [hall] };
        var later = new List<(WingNode Node, int Index)>();
        foreach (var node in template.Nodes)
        {
            ids[node.Name] = [];
            int count = node.Count is null ? 1 : Math.Max(0, counts[node.Count] + node.CountShift);
            for (int index = 0; index < count; index++)
            {
                if (index >= node.InSolve)
                {
                    later.Add((node, index));
                    continue;
                }

                var variant = Random.Choice(node.Variants.ToArray());
                var templates = variant.Select(shape => DrawShape(shape, counts)).ToArray();
                var type = node.Types.Count > 0 ? DrawType(node.Types, node.Type) : node.DrawsBackType ? DrawType(BackTypes, node.Type) : node.Type;
                int room = node.IsHall
                    ? wing.AddHall(phase, [WingSolver.Box(DrawHallLength(variant[0].Length, counts), DrawExtent(variant[0].Width, counts))])
                    : wing.AddRoom(type, phase, NewGroup(), templates, node.IsPiece);
                if (node.Hosts.Count > 0)
                {
                    int host = Resolve(node.Hosts[index % node.Hosts.Count], node.Name, index, ids);
                    wing.Join(host, room, DrawVia(node.Via[Math.Min(index, node.Via.Count - 1)]), node.Mark);
                    SetWingSides(wing, host, room, node.Sides[index % node.Sides.Count], isRight);
                }

                if (node.Packs.Count > 0)
                {
                    wing.MayTouch(Resolve(node.Packs[index % node.Packs.Count], node.Name, index, ids), room);
                }

                ids[node.Name].Add(room);
            }
        }

        foreach (var join in template.Joins)
        {
            wing.Join(ids[join.From][0], ids[join.To][0], DrawVia(join.Via), join.Mark);
            SetWingSides(wing, ids[join.From][0], ids[join.To][0], WingSides.Any, isRight);
        }

        foreach ((string a, string b) in template.Touches)
        {
            wing.MayTouch(ids[a][0], ids[b][0]);
        }

        foreach ((string a, string b) in template.Contacts)
        {
            (int one, int two) = (Resolve(a, a, 0, ids), Resolve(b, b, 0, ids));
            if (one != two)
            {
                wing.Touch(one, two);
            }
        }

        var map = wing.Lay(joint, []);
        if (map.Count == 0)
        {
            Count(BuildingTally.WingUnsolved);
            return [];
        }

        var placed = ids.ToDictionary(pair => pair.Key, pair => pair.Value.Select(id => id == WingSolver.Anchor ? joint : map[id]).ToList());
        var built = template.Nodes
            .Where(node => node.Type != RoomType.Passage && !node.IsHall)
            .SelectMany(node => placed[node.Name])
            .ToList();

        foreach ((var node, int index) in later)
        {
            int[] hosts = (node.Hosts.Count > 0 ? new[] { node.Hosts[index % node.Hosts.Count] } : [])
                .Select(name => Resolve(name, node.Name, index, placed))
                .Concat(node.Beside.SelectMany(name => placed[name]))
                .Distinct()
                .ToArray();

            var shape = Random.Choice(node.Variants.ToArray())[0];
            int count = built.Count;
            AddBeside(built, hosts, phase, node.DrawsBackType ? DrawType(BackTypes, node.Type) : node.Type, DrawExtent(shape.Width, counts), DrawExtent(shape.Length, counts));
            if (built.Count > count)
            {
                placed[node.Name].Add(built[^1]);
            }
        }

        End = placed[template.End].DefaultIfEmpty(placed["hall"][0]).Last();
        if (template.Steps.HasFlag(WingStep.LineBigRooms))
        {
            LineHalls(template.Nodes.Where(node => node.IsHall).SelectMany(node => placed[node.Name]).Prepend(placed["hall"][0]).ToArray(), joint, rooms, built, phase);
        }

        int rest = rooms - built.Count;
        if (template.Steps.HasFlag(WingStep.LinePods) && rest > 0)
        {
            int lined = placed["hall"][0];
            var sides = Grid[lined].AlongU ? new[] { DoorSides.MinY, DoorSides.MaxY } : [DoorSides.MinX, DoorSides.MaxX];
            var plan = PlanOffices(rest, false, true, sides) with { JammedAt = -1 };
            built.AddRange(LayOffices(lined, plan, IsPacked, phase, phase).Rooms);
        }

        return built;
    }

    private Dictionary<string, int> DrawCounts(WingTemplate template, int rooms)
    {
        var counts = new Dictionary<string, int>();
        int taken = template.Nodes.Count(node => node.Count is null && node.Type != RoomType.Passage);
        foreach (var rule in template.Counts)
        {
            int value = rule.IsRest ? rooms - taken - rule.Offset : GrammarRandom.Round(rooms * rule.Share) - rule.Offset;
            value = Math.Clamp(value, rule.Min, Math.Max(rule.Min, rule.Max));
            if (rooms < rule.MinRooms || (rule.Chance < 1 && Random.Next() >= rule.Chance))
            {
                value = 0;
            }

            counts[rule.Name] = value;
            taken += value;
        }

        return counts;
    }

    private int DrawExtent(WingExtent extent, IReadOnlyDictionary<string, int> counts)
    {
        int value = extent.IsUnits ? Random.Inclusive((int)extent.Min, (int)extent.Max) : GrammarRandom.Round(RoomSide * Random.Uniform(extent.Min, extent.Max));
        value = (Math.Max(extent.IsUnits ? 1 : 3, value) + extent.Add) * (extent.Times is null ? 1 : counts[extent.Times]);
        return Math.Clamp(value, extent.MinLength, extent.MaxLength);
    }

    // Lines each hall of a chain with its share of the rooms, solves the rooms left out one by one against the halls and ends the level in the last
    // hall's room nearest its entry
    private void LineHalls(int[] halls, int joint, int rooms, List<int> built, int phase)
    {
        int left = rooms - built.Count;
        var lined = new List<int>();
        for (int index = 0; index < halls.Length && left > 0; index++)
        {
            var rect = RectOf(halls[index]);
            int capacity = Math.Max(2, (Math.Max(rect.W, rect.H) - 4) * 2 / 17);
            int share = Math.Min(capacity, Math.Max(1, left / (halls.Length - index)));
            lined = LineBigRooms(halls[index], share, index == 0, phase);
            built.AddRange(lined);
            Draft.WingPieces.UnionWith(lined);
            left -= share;
        }

        for (int missing = rooms - built.Count; missing > 0; missing--)
        {
            int count = built.Count;
            AddBeside(built, halls.Reverse().ToArray(), phase, RoomType.ComputerRoom, Random.Inclusive(14, 17), Random.Inclusive(18, 20));
            if (built.Count == count)
            {
                break;
            }

            Draft.WingPieces.Add(built[^1]);
        }

        var entry = RectOf(halls.Length > 1 ? halls[^2] : joint);
        bool alongU = Grid[halls[^1]].AlongU;
        if (lined.Count > 0)
        {
            End = lined.MinBy(room => Math.Abs(Center(RectOf(room), alongU) - Center(entry, alongU)));
        }
    }

    private RoomShape DrawShape(WingShape shape, IReadOnlyDictionary<string, int> counts)
    {
        if (shape.Kind == WingShapeKind.Cross)
        {
            return CrossTemplate();
        }

        if (shape.Kind == WingShapeKind.BigRoom)
        {
            return BigRoomBox();
        }

        int width = DrawExtent(shape.Width, counts);
        int length = DrawExtent(shape.Length, counts);
        if (shape.Kind == WingShapeKind.Box)
        {
            return WingSolver.Box(width, length);
        }

        int a = Random.Range(2, 4);
        int b = Random.Range(2, 4);
        return width - a >= 5 && length - b >= 5
            ? new RoomShape(RoomShape.Polygon(new(0, 0), new(0, length), new(width - a, length), new(width - a, length - b), new(width, length - b), new(width, 0)),
                WingSolver.Doors, RoomShape.AllTurns)
            : WingSolver.Box(width, length);
    }

    private int DrawHallLength(WingExtent extent, IReadOnlyDictionary<string, int> counts) =>
        Math.Clamp(DrawExtent(extent, counts), MinHall, extent.MaxLength == int.MaxValue ? GateHallMax : extent.MaxLength);

    private int AddTemplateHall(GraphWing wing, WingHall hall, IReadOnlyDictionary<string, int> counts, int phase)
    {
        int length = DrawHallLength(hall.Length, counts);
        if (!hall.IsBeyond)
        {
            return AddWingHall(wing, phase, length, hall.Width > 0 ? hall.Width : HallWidth);
        }

        int id = wing.AddHall(phase, [new RoomShape(RoomShape.Rectangle(HallWidth, length), WingSolver.Doors, [ShapeTurn.Identity, ShapeTurn.MirrorX])]);
        wing.Join(WingSolver.Anchor, id, JoinVia.Door, JoinMark.Contact);
        return id;
    }

    private static int Resolve(string name, string node, int index, IReadOnlyDictionary<string, List<int>> ids) => name switch
    {
        "previous" => index == 0 ? ids["hall"][0] : ids[node][index - 1],
        _ when name.EndsWith('#') => ids[name[..^1]][Math.Min(index, ids[name[..^1]].Count - 1)],
        _ when name.EndsWith('^') => ids[name[..^1]].Count > 0 ? ids[name[..^1]][^1] : ids.Values.ElementAt(1)[0],
        _ => ids[name][0]
    };

    private JoinVia DrawVia(WingVia via) => via switch
    {
        WingVia.Neck => JoinVia.Neck,
        WingVia.Passage => JoinVia.Passage,
        WingVia.NeckOrDoor => Random.Next() < NeckShare ? JoinVia.Neck : JoinVia.Door,
        WingVia.PassageOrNeck => Random.Next() < 0.5f ? JoinVia.Passage : JoinVia.Neck,
        _ => JoinVia.Door
    };

    private static void SetWingSides(GraphWing wing, int a, int b, WingSides sides, bool isRight)
    {
        switch (sides)
        {
            case WingSides.Far:
                wing.SetSides(a, b, DoorSides.MaxX, null);
                break;
            case WingSides.Near:
                wing.SetSides(a, b, DoorSides.MinX, null);
                break;
            case WingSides.Down:
                wing.SetSides(a, b, DoorSides.MinY, DoorSides.MaxY);
                break;
            case WingSides.Across:
                wing.SetSides(a, b, isRight ? DoorSides.MaxX : DoorSides.MinX, isRight ? DoorSides.MinX : DoorSides.MaxX);
                break;
            case WingSides.HostEnds:
                wing.SetSides(a, b, DoorSides.MinX | DoorSides.MaxX, null);
                break;
            case WingSides.RoomEnds:
                wing.SetSides(a, b, null, DoorSides.MinX | DoorSides.MaxX);
                break;
        }
    }

}

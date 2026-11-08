using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using System.Drawing;

namespace ASToolkit.Core.Generation.Buildings;

internal abstract partial class BuildingGrammar
{
    protected enum JoinVia
    {
        Door,
        Neck,
        Passage
    }

    protected enum JoinMark
    {
        Contact,
        Junction,
        Lock
    }

    protected sealed class GraphWing
    {
        private readonly BuildingGrammar _grammar;
        private readonly WingSolver _solver;
        private readonly List<Spec> _specs = [];
        private readonly List<(int A, int B, JoinVia Via, JoinMark Mark)> _joins = [];
        private readonly Dictionary<int, int> _throatRooms = [];
        private readonly Dictionary<int, int> _snugSides = [];
        private int _next = 1;

        public GraphWing(BuildingGrammar grammar, bool isBeyond = false)
        {
            _grammar = grammar;
            _solver = new WingSolver(isBeyond);
        }

        private sealed class Spec
        {
            public required int Id { get; init; }

            public required RoomType Type { get; init; }

            public required int Phase { get; init; }

            public required int Group { get; init; }

            public bool IsHall { get; init; }

            public bool IsWingPiece { get; init; }
        }

        public int AddRoom(RoomType type, int phase, int group, IReadOnlyList<RoomShape> templates, bool isWingPiece = false)
        {
            int id = _next++;
            _specs.Add(new Spec { Id = id, Type = type, Phase = phase, Group = group, IsWingPiece = isWingPiece });
            _solver.SetTemplates(id, templates.ToList());

            _snugSides[id] = templates
                .Min(GetSnugSide);

            return id;
        }

        // Returns the longest flush wall the template can guarantee, the shorter box side capped at its longest outline edge
        private static int GetSnugSide(RoomShape template)
        {
            int side = Math.Min(template.Width, template.Height);
            return template.IsBox ? side : Math.Min(side, template.LongestSide);
        }

        public void Snug(int a, int b)
        {
            _solver.Touch(a, b, Math.Min(3, Math.Min(GetSnugSide(a), GetSnugSide(b))));
        }

        private int GetSnugSide(int room) => _snugSides.GetValueOrDefault(room, int.MaxValue);

        public int AddHall(int phase, IReadOnlyList<RoomShape> templates)
        {
            int id = _next++;
            _specs.Add(new Spec { Id = id, Type = RoomType.Passage, Phase = phase, Group = 0, IsHall = true });
            _solver.SetTemplates(id, templates.ToList());
            return id;
        }

        public void Join(int a, int b, JoinVia via, JoinMark mark = JoinMark.Contact)
        {
            _solver.Join(a, b, via switch { JoinVia.Neck => [NeckDepth], JoinVia.Passage => [3], _ => [] });
            _joins.Add((a, b, via, mark));
        }

        public void SetSides(int a, int b, DoorSides? sidesOfA, DoorSides? sidesOfB) => _solver.SetSides(a, b, sidesOfA, sidesOfB);

        public void MayTouch(int a, int b) => _solver.MayTouch(a, b);

        public void Touch(int a, int b) => _solver.Touch(a, b);

        public Dictionary<int, int> Lay(int anchor, IReadOnlyList<UnitRect> reserved, int attempts = 2, int? touching = null, int reach = 0)
        {
            var anchorRect = _grammar.RectOf(anchor);
            if (reach == 0)
            {
                reach = 40 + Math.Max(anchorRect.W, anchorRect.H);
            }
            // Keep a touch group's rects out of the obstacles only where they meet the anchor, letting a room nest against the host's step
            var obstacles = _grammar.GetObstacles(anchor)
                .Where(rect => touching is null || _grammar.GroupAt(rect) != touching || !(rect.SharesWall(anchorRect) || rect.IntersectsWith(anchorRect)))
                .Where(rect => rect.IntersectsWith(anchorRect.Inflate(reach)))
                .Concat(reserved)
                .ToArray();

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var placed = _solver.Lay(_grammar.RectOf(anchor), obstacles, _grammar.Random.Range(0, int.MaxValue));
                if (placed.Count == 0)
                {
                    continue;
                }

                var rects = placed
                    .Select(entry => entry.Value)
                    .SelectMany(place => place.Throat is null ? place.Rects : place.Rects.Append(place.Throat.Value))
                    .ToArray();

                bool isClear = rects.All(rect => !_grammar.Grid.LeavesSlit(rect) && rect.Units.All(cell => _grammar.Grid.GetOwner(cell) is null));
                if (!isClear)
                {
                    _grammar.Count(BuildingTally.WingCollides);
                    continue;
                }

                return Realize(anchor, placed);
            }

            return [];
        }

        private Dictionary<int, int> Realize(int anchor, Dictionary<int, WingPlace> placed)
        {
            var map = new Dictionary<int, int> { [WingSolver.Anchor] = anchor };
            foreach (var spec in _specs)
            {
                var place = placed[spec.Id];
                int built = spec.IsHall
                    ? _grammar.AddHall(place.Rects[0], spec.Phase, place.Rects[0].W >= place.Rects[0].H)
                    : place.Rects.Length > 1
                        ? _grammar.AddDepot(place.Rects, spec.Type, spec.Phase, spec.Group)
                        : _grammar.AddRoom(place.Rects[0], spec.Type, spec.Phase, group: spec.Group);
                if (spec.IsWingPiece)
                {
                    _grammar.Draft.WingPieces.Add(built);
                }

                map[spec.Id] = built;
            }

            foreach (var spec in _specs)
            {
                var place = placed[spec.Id];
                if (place.Throat is null)
                {
                    continue;
                }

                var throat = place.Throat.Value;
                if (Math.Min(throat.W, throat.H) >= 3)
                {
                    _throatRooms[spec.Id] = _grammar.AddRoom(throat, RoomType.Passage, spec.Phase, isJoint: false, _grammar.Grid[map[spec.Id]].Group);
                }
                else
                {
                    int owner = map[spec.Id];
                    _grammar.Grid.Add(throat, GridKind.Neck, _grammar.Grid[owner].Group, spec.Phase, null, alongU: true, isJoint: false, owner);
                    _grammar.Draft.Shapes[owner].Add(throat);
                }
            }

            foreach (var join in _joins)
            {
                (int a, int b, var via, _) = join;
                switch (join.Mark)
                {
                    case JoinMark.Lock:
                        _grammar.Locks.Add((map[a], map[b]));
                        break;
                    case JoinMark.Junction:
                        if (via == JoinVia.Passage && _throatRooms.TryGetValue(b, out int throatRoom))
                        {
                            _grammar.Junctions.Add((map[a], throatRoom));
                            _grammar.Junctions.Add((throatRoom, map[b]));
                        }
                        else
                        {
                            _grammar.Junctions.Add((map[a], map[b]));
                        }

                        break;
                }
            }

            return map;
        }
    }

    // Returns the strip past a hub's far end that its pods keep off for the wing leaving there
    private UnitRect EndStrip(int hub)
    {
        var rect = RectOf(hub);
        return new UnitRect(rect.U + rect.W, rect.V - 48, 1, rect.H + 96);
    }

    private int? GroupAt(UnitRect rect)
    {
        int? owner = Grid.GetOwner(new Cell(rect.U, rect.V));
        return owner is null ? null : Grid[owner.Value].Group;
    }

    private static RoomShape CrossTemplate() =>
        new(WingSolver.Outline((2, 0), (2, 2), (0, 2), (0, 5), (2, 5), (2, 7), (5, 7), (5, 5), (7, 5), (7, 2), (5, 2), (5, 0)),
            [
                new ShapeDoor(new Point(2, 0), new Point(5, 0)),
                new ShapeDoor(new Point(7, 2), new Point(7, 5)),
                new ShapeDoor(new Point(2, 7), new Point(5, 7)),
                new ShapeDoor(new Point(0, 2), new Point(0, 5))
            ],
            RoomShape.AllTurns);

    private DoorSides LongSides(int hall) => Grid[hall].AlongU ? DoorSides.MinY | DoorSides.MaxY : DoorSides.MinX | DoorSides.MaxX;

    private void AddBeside(List<int> built, IReadOnlyList<int> hosts, int phase, RoomType type, int along, int depth)
    {
        foreach (int host in hosts)
        {
            int? room = SolveRoomBeside(host, phase, type, along, depth, along >= 5 && Random.Next() < NeckShare, false);
            if (room is not null)
            {
                Junctions.Add((host, room.Value));
                built.Add(room.Value);
                return;
            }
        }
    }

    private int? SolveRoomBeside(int host, int phase, RoomType type, int along, int depth, bool viaNeck, bool isLocked)
    {
        var wing = new GraphWing(this);
        int room = wing.AddRoom(type, phase, NewGroup(), [WingSolver.Box(along, depth)]);
        wing.Join(WingSolver.Anchor, room, viaNeck ? JoinVia.Neck : JoinVia.Door, isLocked ? JoinMark.Lock : JoinMark.Contact);
        wing.SetSides(WingSolver.Anchor, room, DoorSides.All, null);
        var map = wing.Lay(host, [], 1, Grid[host].Group, 22);
        return map.Count == 0 ? null : map[room];
    }

    // Lines a hall's sides with its big rooms in one solve, the first hall leaving through a loop of two of them, dropping rooms from the end until
    // a layout is found
    private List<int> LineBigRooms(int hall, int rooms, bool hasLoop, int phase)
    {
        var sides = LongSides(hall);
        for (int kept = rooms; kept > 0; kept--)
        {
            var wing = new GraphWing(this);
            var ids = new List<int>();
            if (hasLoop && kept >= 3)
            {
                int one = wing.AddRoom(RoomType.ComputerRoom, phase, NewGroup(), [BigRoomBox()]);
                int two = wing.AddRoom(RoomType.ComputerRoom, phase, NewGroup(), [BigRoomBox()], isWingPiece: true);
                foreach (int room in new[] { one, two })
                {
                    wing.Join(WingSolver.Anchor, room, JoinVia.Passage, JoinMark.Junction);
                    wing.SetSides(WingSolver.Anchor, room, sides, null);
                }

                wing.Join(one, two, JoinVia.Door, JoinMark.Junction);
                wing.MayTouch(one, two);
                ids.AddRange([one, two]);
            }

            while (ids.Count < kept)
            {
                int big = wing.AddRoom(RoomType.ComputerRoom, phase, NewGroup(), [BigRoomBox()], isWingPiece: true);
                double roll = Random.Next();
                wing.Join(WingSolver.Anchor, big, roll < BigRoomPassageEntry ? JoinVia.Passage : roll < BigRoomPassageEntry + NeckShare ? JoinVia.Neck : JoinVia.Door,
                    JoinMark.Junction);
                wing.SetSides(WingSolver.Anchor, big, sides, null);
                ids.Add(big);
            }

            var map = wing.Lay(hall, [], 1);
            if (map.Count > 0)
            {
                return ids.Select(id => map[id]).ToList();
            }

            Count(BuildingTally.RoomsMoved);
        }

        return [];
    }

    private RoomShape BigRoomBox() => WingSolver.Box(Random.Inclusive(14, 17), Random.Inclusive(18, 20));

    private static int Center(UnitRect rect, bool alongU) => alongU ? rect.U + rect.W / 2 : rect.V + rect.H / 2;

    private static int AddWingHall(GraphWing wing, int phase, int length, int width)
    {
        int hall = wing.AddHall(phase, [WingSolver.Box(length, width)]);
        wing.Join(WingSolver.Anchor, hall, JoinVia.Door);
        wing.SetSides(WingSolver.Anchor, hall, DoorSides.MaxX | DoorSides.MinY | DoorSides.MaxY, DoorSides.MinX | DoorSides.MaxX);
        return hall;
    }

}

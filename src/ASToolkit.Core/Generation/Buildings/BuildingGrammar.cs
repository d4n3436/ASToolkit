using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides the shared productions of the building grammars, such as halls lined by compact pods standing apart, the lab wings, depot sections,
/// halls of big rooms in sequence, closets and secret rooms, and the gate programs every building can take.
/// </summary>
internal abstract partial class BuildingGrammar
{
    protected const int MinHall = 14;

    protected const int NeckDepth = 2;

    private const int GateHallMax = 40;
    private const int SecondWingMaxRooms = 14;
    private const int SecondWingMinRooms = 7;
    private const int IntroSuiteMaxRooms = 4;
    private const int RingMaxRooms = 3;
    private const float BigRoomPassageEntry = 0.2f;

    private static readonly (RoomType Type, float Weight)[] DepotTypes = [(RoomType.Depot, 0.7f), (RoomType.Storage, 0.3f)];
    
    protected BuildingGrammar(BuildingRequest request, Random rng)
    {
        Draft = new BuildingDraft(request, new GrammarRandom(rng));
        IsIntro = request.IsIntro;
    }

    public BuildingDraft Draft { get; }

    public BuildingGrid Grid => Draft.Grid;

    public Dictionary<string, int> Counts => Draft.Counts;

    public List<(int From, int To)> Locks => Draft.Locks;

    public List<(int Room, int Passage)> Junctions => Draft.Junctions;

    public List<RoomPair> ClosedWalls => Draft.ClosedWalls;

    public List<RoomPair> JammedWalls => Draft.JammedWalls;

    public List<RoomPair> OpenWalls => Draft.OpenWalls;

    public List<RoomPair> Shortcuts => Draft.Shortcuts;

    public int? Start { get => Draft.Start; protected set => Draft.Start = value; }

    public List<int> Keys => Draft.Keys;

    public int? GateRoom { get => Draft.GateRoom; protected set => Draft.GateRoom = value; }

    public int? Trigger { get => Draft.Trigger; protected set => Draft.Trigger = value; }

    public HashSet<int> SecondWing => Draft.SecondWing;

    public HashSet<int> LockedSiteRooms => Draft.LockedSiteRooms;

    public int? End { get => Draft.End; protected set => Draft.End = value; }

    // Holds the last depot wing's rooms in walking order, from the room meeting its hall onward
    protected List<int> Run { get => Draft.Run; private set => Draft.Run = value; }

    protected BuildingRequest Request => Draft.Request;

    protected GrammarRandom Random => Draft.Random;

    protected bool IsIntro { get; }

    protected float RoomSide { get => Draft.RoomSide; set => Draft.RoomSide = value; }

    protected bool IsPacked { get; set; }

    protected float NeckShare { get; set; }

    protected int HallWidth { get; set; } = 3;

    protected virtual IReadOnlyList<(RoomType Type, float Weight)> FrontTypes { get; } =
        [(RoomType.Office, 0.45f), (RoomType.Lab, 0.2f), (RoomType.Storage, 0.2f)];

    protected virtual IReadOnlyList<(RoomType Type, float Weight)> BackTypes { get; } =
        [(RoomType.Storage, 0.45f), (RoomType.Office, 0.35f), (RoomType.Depot, 0.2f)];

    public abstract void Build();

    public void Count(BuildingTally tally, int amount = 1) => Draft.Count(tally, amount);

    public void Count(BuildingTally tally, LabWing wing, int amount = 1) => Draft.Count(tally, wing, amount);

    // Counts the rooms the program asked for that the wings left unbuilt
    public void TopUp()
    {
        int built = CountRegularRooms();
        if (Request.Rooms > built)
        {
            Count(BuildingTally.RoomsPastWings, Request.Rooms - built);
        }
    }

    protected void GrowCrossPassages()
    {
        if (CountRegularRooms() < 13)
        {
            return;
        }

        int grown = 0;
        var passages = Grid.Rooms
            .Where(room => room is { Kind: GridKind.Room, Type: RoomType.Passage, IsJoint: false } && !Draft.KeepsOutline.Contains(room.Id))
            .Select(room => room.Id)
            .Where(id => !Grid.Rooms.Any(other => other.Kind == GridKind.Arm && other.Owner == id))
            .ToList();

        foreach (int passage in Random.Shuffled(passages))
        {
            if (grown >= 2)
            {
                break;
            }

            if (AddFreeArms(passage, [], 2) > 0)
            {
                grown++;
            }
        }
    }

    // Grows arms off a passage into the free gaps around it, opening first those that end against an unjoined room of its phase, and returns how
    // many reach a room
    private int AddFreeArms(int room, IReadOnlyList<UnitRect> avoid, int most)
    {
        var rect = RectOf(room);
        var options = new List<(UnitRect Arm, bool AlongU, int? Target)>();
        foreach (var side in Enum.GetValues<Side>())
        {
            int span = side.RunsAlongU ? rect.W : rect.H;
            for (int offset = 0; offset <= Math.Max(0, span - 3); offset++)
            {
                foreach (int depth in (int[])[2, 1])
                {
                    var arm = rect.Beside(side, 3, depth, (side.RunsAlongU ? rect.U : rect.V) + offset);
                    if (!Grid.IsNeckFree(arm, Grid[room].Group) || avoid.Any(other => other.IntersectsWith(arm)))
                    {
                        continue;
                    }

                    int? target = Grid.Rooms
                        .Where(other => other.Kind == GridKind.Room && other.Id != room && other.Phase == Grid[room].Phase && other.Type != RoomType.Passage)
                        .Where(other => Shapes(other.Id).Any(part => part.SharesWall(arm)) && !Shapes(other.Id).Any(part => Shapes(room).Any(own => own.SharesWall(part))))
                        .Select(other => (int?)other.Id)
                        .FirstOrDefault();

                    options.Add((arm, side.RunsAlongU, target));
                }
            }
        }

        int added = 0;
        int reaching = 0;
        var taken = new List<UnitRect>();
        foreach (var option in Random.Shuffled(options).OrderByDescending(option => option.Target is not null))
        {
            if (added >= most || taken.Any(other => other.IntersectsWith(option.Arm) || other.SharesWall(option.Arm)) || !Grid.IsFree(option.Arm, Grid[room].Group, 0, []))
            {
                continue;
            }

            AddArm(room, option.Arm, option.AlongU);
            taken.Add(option.Arm);
            if (option.Target is not null)
            {
                Junctions.Add((option.Target.Value, room));
                reaching++;
            }

            added++;
        }

        return reaching;
    }

    public virtual void PlaceSideRooms() => new SideRoomPlacer(Draft, BackTypes).PlaceClosets();

    private List<int> BuildGated(int joint, int rooms, int phase)
    {
        var kinds = new List<LabWing>();
        var named = Request.SecondWing;
        if (named is not null && GetSecondWings(LevelStyle.LockedDoors, IsIntro).Contains(named.Value))
        {
            kinds.Add(named.Value);
            rooms = Math.Clamp(rooms, MinRoomsOf(named.Value), MaxRoomsOf(named.Value));
        }
        else if (IsIntro)
        {
            kinds.Add(LabWing.Suite);
        }
        else
        {
            rooms = Math.Clamp(rooms, SecondWingMinRooms, GatedWings.Max(MaxRoomsOf));
            kinds.AddRange(GatedWings.Where(kind => rooms >= MinRoomsOf(kind) && rooms <= MaxRoomsOf(kind)));
        }

        foreach (var kind in Random.Shuffled(kinds).Concat(Random.Shuffled(GatedWings)).Distinct())
        {
            int before = CountRegularRooms();
            var built = kind switch
            {
                LabWing.Suite => LayWing(Suite, joint, rooms, phase),
                LabWing.Research => BuildResearchWing(joint, rooms, phase),
                LabWing.BigRoomHalls => LayWing(BigRoomHalls, joint, rooms, phase),
                LabWing.Gallery => LayWing(Gallery, joint, rooms, phase),
                LabWing.Echelon => LayWing(Echelon, joint, rooms, phase),
                _ => LayWing(Airlock, joint, rooms, phase)
            };

            if (built.Count > 0)
            {
                Count(BuildingTally.Wing, kind);
                int missing = rooms - (CountRegularRooms() - before);
                Count(BuildingTally.Short, kind, Math.Max(0, missing));
                return built;
            }

            Count(BuildingTally.Fail, kind);
        }

        return [];
    }

    internal static IReadOnlyList<LabWing> GetSecondWings(LevelStyle gate, bool isIntro) => gate switch
    {
        LevelStyle.LockedDoors => isIntro ? [LabWing.Suite] : GatedWings,
        LevelStyle.Blackout => DepotWings,
        LevelStyle.Ungated => [BigRoomHalls.Kind],
        _ => []
    };

    private int CountRegularRooms() => Grid.Rooms.Count(room => room is { Kind: GridKind.Room, IsJoint: false } && room.Type != RoomType.Passage);

    // Holds the wings that can stand behind a locked gate, none with a breaker of its own
    private static readonly LabWing[] GatedWings = [LabWing.Gallery, LabWing.Echelon, LabWing.Airlock, LabWing.Research, LabWing.BigRoomHalls];

    private static readonly LabWing[] DepotWings = [LabWing.DepotSections, LabWing.Dock, LabWing.ServiceCorridor];

    private static int MinRoomsOf(LabWing kind) => kind == LabWing.Research ? 7 : SolvedWings.First(wing => wing.Kind == kind).MinRooms;

    private static int MaxRoomsOf(LabWing kind) => kind == LabWing.Research ? 12 : SolvedWings.First(wing => wing.Kind == kind).MaxRooms;

    protected void BuildSequence(int rooms)
    {
        // Ring both sides, one jammed and one open for the hub's loop
        var plan = PlanOffices(Math.Min(FirstWingRooms(), Math.Max(4, rooms - 3)) - 1, false, true, HubSides, 2);
        (int hub, int joint) = AddHub(0, plan.Length);
        Start = AddEndRoom(hub, Side.West, 0, viaNeck: false);
        var (built, _) = LayOffices(hub, plan, IsPacked, 0);
        int wing = Grid.Rooms.Count + 1;
        var rest = rooms - 1 - built.Count > 0 ? LayWing(BigRoomHalls, joint, rooms - 1 - built.Count, 0) : [];
        MarkSecondWing(wing);
        if (End is null)
        {
            EndFarthest(rest.Count > 0 ? rest : built);
        }
    }

    private void MarkSecondWing(int first) => SecondWing.UnionWith(Grid.Rooms
        .Where(room => room.Id >= first && room.Kind is GridKind.Room or GridKind.Hall)
        .Select(room => room.Id));

    protected void EndFarthest(IReadOnlyList<int> rooms)
    {
        if (Start is null || rooms.Count == 0)
        {
            return;
        }

        int[] walkable = Grid.Rooms
            .Where(room => room.Kind is GridKind.Room or GridKind.Hall && room.Id != Start)
            .Select(room => room.Id)
            .Prepend(Start.Value)
            .ToArray();

        var depths = DoorDepths(walkable);
        End = rooms.Where(room => room != Start).DefaultIfEmpty(rooms[0]).MaxBy(depths.GetValueOrDefault);
    }

    private static readonly RoomShape[] OfficeRooms =
    [
        WingSolver.Box(5, 5), WingSolver.Box(6, 5), WingSolver.Box(6, 6), WingSolver.Box(7, 5), WingSolver.Box(6, 4), WingSolver.Box(5, 4),
        OfficeL(7, 6, 3, 3), OfficeL(6, 7, 2, 3), OfficeL(7, 7, 3, 3), OfficeL(6, 6, 3, 2)
    ];

    private static readonly RoomShape[] RingHallRooms = [WingSolver.Box(6, 6), WingSolver.Box(7, 6), WingSolver.Box(6, 8), WingSolver.Box(7, 8)];
    private static readonly RoomShape[] SmallRooms = [WingSolver.Box(4, 5)];
    private static readonly RoomShape[] BigOfficeRooms = [WingSolver.Box(8, 7), OfficeL(8, 8, 3, 2), OfficeL(9, 7, 3, 2)];
    private static readonly RoomShape[] BigRingHallRooms = [WingSolver.Box(7, 8), WingSolver.Box(8, 7)];

    private static RoomShape OfficeL(int width, int height, int notchWidth, int notchHeight) =>
        new(RoomShape.Polygon(new(0, 0), new(0, height), new(width - notchWidth, height), new(width - notchWidth, height - notchHeight), new(width, height - notchHeight), new(width, 0)),
            WingSolver.Doors, RoomShape.AllTurns);

    protected void BuildLockedOffices(int rooms)
    {
        var plan = PlanOffices(FirstWingRooms() - 1, true, false, HubSides);
        (int hub, int joint) = AddHub(1, plan.Length);
        Start = AddEndRoom(hub, Side.West, 0, viaNeck: false);
        Locks.Add((hub, joint));
        GateRoom = joint;
        (var built, int? key) = LayOffices(hub, plan, IsPacked && !IsIntro, 1);
        key ??= built.DefaultIfEmpty(Start.Value).Last();
        Grid[key.Value].Type = RoomType.MachineRoom;
        Keys.Add(key.Value);
        int left = IsIntro ? Math.Clamp(rooms - 1 - built.Count, 2, IntroSuiteMaxRooms) : rooms - 1 - built.Count;
        int wing = Grid.Rooms.Count + 1;
        var gatedRooms = BuildGated(joint, left, 1);
        if (gatedRooms.Count == 0 && left > 4)
        {
            Count(BuildingTally.GatedRetry);
            gatedRooms = BuildGated(joint, left - 2, 1);
        }

        MarkSecondWing(wing);

        if (End is null)
        {
            EndFarthest(gatedRooms);
        }
    }

    protected void BuildDarkOffices(int rooms)
    {
        var plan = PlanOffices(FirstWingRooms() - 1, false, true, HubSides);
        (int hub, _) = AddHub(null, plan.Length);
        Start = AddEndRoom(hub, Side.West, 0, viaNeck: true);
        var (built, _) = LayOffices(hub, plan, true, 2);
        int second = Math.Clamp(rooms - 1 - built.Count, 6, SecondWingMaxRooms);
        int last = Math.Max(2, GrammarRandom.Round(second * 0.35f));
        var named = Request.SecondWing;
        var kind = named is not null && DepotWings.Contains(named.Value)
            ? named.Value
            : Random.Weighted([(LabWing.DepotSections, 4), (LabWing.ServiceCorridor, 1), (LabWing.Dock, 1)]);

        int wing = Grid.Rooms.Count + 1;
        var (depots, nested) = BuildDepotLoop(hub, Math.Max(second - last, DepotWing.GetMinSlots(kind)), 1, 2, last, kind);
        if (depots.Count == 0)
        {
            Count(BuildingTally.DarkBlockUnplaced);
            return;
        }

        Trigger = Run[0];
        MarkSecondWing(wing);
        LockedSiteRooms.UnionWith(nested.Where(SecondWing.Contains));
        Keys.Add(Run[^1]);
        Grid[Run[^1]].Type = RoomType.MachineRoom;
    }

    private int FirstWingRooms() => IsIntro ? Math.Clamp(Request.Rooms - IntroSuiteMaxRooms, 6, 10) : Math.Clamp(Request.Rooms - SecondWingMaxRooms + 2, 12, 14);

    private (List<int> Rooms, int? Key) LayOffices(int hub, OfficePlan plan, bool packed, int lockedPhase, int openPhase = 0)
    {
        var pods = plan.Pods.ToArray();
        int keyAt = plan.KeyAt;
        var built = new List<int>();
        var laid = new List<(int Index, List<int> Rooms)>();
        int? key = null;
        Draft.WingPods.Clear();
        int[] order = Enumerable.Range(0, pods.Length).OrderByDescending(plan.Locked.Contains).ToArray();
        var done = new HashSet<int>();
        foreach (int index in order)
        {
            done.Add(index);
            int size = pods[index].Rooms;
            int phase = plan.Locked.Contains(index) ? lockedPhase : openPhase;
            bool jams = index == plan.JammedAt;
            var pod = new List<int>();
            // Shrink a ring that finds no layout to a clump, and retry a failed clump once at half its rooms
            for (; size >= 1 && pod.Count == 0; size = pods[index].IsRing ? size > 3 ? size - 1 : 0 : size > 1 && size == pods[index].Rooms ? size / 2 : 0)
            {
                pod = LayOfficePod(hub, pods[index].Side, size, pods[index].IsRing && size >= 3, phase, packed, jams, index == keyAt);
            }

            for (size = 3; pod.Count == 0 && pods[index].IsRing && size >= 1; size -= 2)
            {
                pod = LayOfficePod(hub, pods[index].Side, size, false, phase, packed, false, index == keyAt);
            }

            Count(pod.Count == 0 ? BuildingTally.OfficePodUnplaced : BuildingTally.OfficePod);
            Count(pods[index].IsRing ? BuildingTally.OfficeRingAsked : BuildingTally.OfficeClumpAsked, pods[index].Rooms);
            Count(pods[index].IsRing ? BuildingTally.OfficeRingBuilt : BuildingTally.OfficeClumpBuilt, pod.Count);
            if (index == keyAt && pod.Count > 0)
            {
                key = pod[^1];
            }

            built.AddRange(pod);
            Draft.WingPods.UnionWith(pod);
            if (pod.Count > 0)
            {
                laid.Add((index, pod));
            }

            int spill = pods[index].Rooms - pod.Count;
            int next = order.Where(other => !done.Contains(other) && !pods[other].IsRing && pods[other].Rooms < 6).DefaultIfEmpty(-1).First();
            if (spill > 0 && next >= 0)
            {
                pods[next] = pods[next] with { Rooms = Math.Min(6, pods[next].Rooms + spill) };
            }
        }

        if (plan.Locked.Count > 0 && !laid.Any(entry => plan.Locked.Contains(entry.Index)))
        {
            var spare = laid.Where(entry => entry.Index != keyAt).Select(entry => entry.Rooms).LastOrDefault();
            if (spare is not null)
            {
                foreach (int room in spare)
                {
                    Grid[room].Phase = lockedPhase;
                }

                foreach (var join in Junctions.Where(join => join.Room == hub && spare.Contains(join.Passage)).ToArray())
                {
                    Junctions.Remove(join);
                    Locks.Add((hub, join.Passage));
                }
            }
        }

        Draft.OfficeWing.Add(hub);
        Draft.OfficeWing.UnionWith(built);
        return (built, key);
    }

    // Draws the office pods for about the given rooms and the hub length their longer side needs, locking a pod on a dark level and jamming a
    // ring's second hall door on a lit one
    private static readonly DoorSides[] HubSides = [DoorSides.MinY, DoorSides.MaxY];

    private OfficePlan PlanOffices(int rooms, bool hasKey, bool isLit, IReadOnlyList<DoorSides> sides, int leastRings = 1)
    {
        var drawn = new List<(DoorSides Side, bool IsRing)>();
        foreach (var side in Random.Shuffled(sides))
        {
            bool isRing = drawn.Count < leastRings || Random.Next() < 0.5f;
            int count = isRing || (Random.Next() < 0.4f && drawn.Count == 0) ? 1 : 2;
            drawn.AddRange(Enumerable.Repeat((side, isRing), count));
        }

        // Leave a side for clumps on a level with a key, the breaker standing at a clump's far end
        if (hasKey && drawn.All(pod => pod.IsRing))
        {
            var side = drawn[^1].Side;
            drawn[^1] = (side, false);
            if (Random.Next() < 0.6f)
            {
                drawn.Add((side, false));
            }
        }

        int held = drawn.Sum(pod => pod.IsRing ? RingMaxRooms : 6);
        while (held < rooms && drawn.Count < 4)
        {
            drawn.Add((drawn.GroupBy(pod => pod.Side).MinBy(side => side.Count())!.Key, false));
            held += 6;
        }

        float weights = drawn.Sum(pod => pod.IsRing ? 3.0f : 1.5f);
        var pods = drawn
            .Select(pod => new OfficePod(pod.Side, pod.IsRing, Math.Clamp(GrammarRandom.Round(rooms * (pod.IsRing ? 3.0f : 1.5f) / weights), pod.IsRing ? 3 : 1, pod.IsRing ? RingMaxRooms : 6)))
            .ToArray();

        for (int index = 0, missing = rooms - pods.Sum(pod => pod.Rooms); missing > 0 && index < pods.Length * 6; index++)
        {
            int at = index % pods.Length;
            if (!pods[at].IsRing && pods[at].Rooms < 6)
            {
                pods[at] = pods[at] with { Rooms = pods[at].Rooms + 1 };
                missing--;
            }
        }

        int keyAt = hasKey ? Random.Choice(Enumerable.Range(0, pods.Length).Where(index => !pods[index].IsRing).DefaultIfEmpty(0).ToArray()) : -1;
        if (keyAt >= 0 && !pods[keyAt].IsRing && !IsIntro)
        {
            pods[keyAt] = pods[keyAt] with { Rooms = Math.Max(3, pods[keyAt].Rooms) };
        }
        int along = pods
            .GroupBy(pod => pod.Side)
            .Max(side => side.Sum(pod => pod.IsRing ? 16 + 3 * (pod.Rooms - 2) : Math.Min(16, 6 + 3 * (pod.Rooms - 1))) + 3 * (side.Count() - 1));

        var locked = isLit ? [] : Enumerable.Range(2, Math.Max(0, pods.Length - 2)).Where(index => index != keyAt).ToHashSet();
        if (!isLit && locked.Count == 0)
        {
            locked.Add(Random.Choice(Enumerable.Range(0, pods.Length).Where(index => index != keyAt).ToArray()));
        }

        int jammedAt = isLit ? 0 : -1;
        bool isCrowded = pods.GroupBy(pod => pod.Side).Any(side => side.Any(pod => pod.IsRing) && side.Any(pod => !pod.IsRing));
        return new OfficePlan(pods, keyAt, locked, jammedAt, Math.Clamp(along + 4, 16, isCrowded ? 40 : 32));
    }

    private readonly record struct OfficePod(DoorSides Side, bool IsRing, int Rooms);

    private readonly record struct OfficePlan(IReadOnlyList<OfficePod> Pods, int KeyAt, IReadOnlySet<int> Locked, int JammedAt, int Length);

    private List<int> LayOfficePod(int hub, DoorSides side, int rooms, bool isRing, int phase, bool packed, bool jams, bool isKey)
    {
        int group = NewGroup();
        var via = packed && Random.Next() < 0.5f ? JoinVia.Door : JoinVia.Neck;
        var hallMark = phase > Grid[hub].Phase ? JoinMark.Lock : JoinMark.Junction;
        var open = new HashSet<RoomPair>();
        var built = new List<int>();
        if (isRing)
        {
            var wing = new GraphWing(this);
            var ids = new List<int>();
            ids.AddRange([AddOffice(wing, BigRingHallRooms), AddOffice(wing, RingHallRooms)]);
            int backs = Math.Min(4, rooms - 2);
            ids.InsertRange(1, Enumerable.Range(0, backs).Select(index => AddOffice(wing, index == backs - 1 ? SmallRooms : OfficeRooms)));
            foreach (int end in (int[])[ids[0], ids[^1]])
            {
                // Stand the jammed end flush with the hall, its whole front a wall for the jammed door
                bool isJammed = jams && end == ids[^1];
                wing.Join(WingSolver.Anchor, end, isJammed ? JoinVia.Door : via, isJammed ? JoinMark.Contact : hallMark);
                wing.SetSides(WingSolver.Anchor, end, side, null);
            }

            for (int index = 0; index + 1 < ids.Count; index++)
            {
                wing.Join(ids[index], ids[index + 1], JoinVia.Door, JoinMark.Junction);
                wing.MayTouch(ids[index], ids[index + 1]);
            }

            var map = wing.Lay(hub, [EndStrip(hub)], rooms <= 3 ? 3 : 1);
            if (map.Count == 0)
            {
                return [];
            }

            built.AddRange(ids.Select(id => map[id]));
            open.UnionWith(built.Zip(built.Skip(1), (a, b) => new RoomPair(a, b)).Append(new RoomPair(hub, built[0])));
            if (jams)
            {
                JammedWalls.Add(new RoomPair(hub, built[^1]));
            }
            else
            {
                open.Add(new RoomPair(hub, built[^1]));
            }
        }
        else
        {
            // Try the breaker's clump folded first, then as a plain chain
            for (int attempt = isKey ? 0 : 1; attempt < 2 && built.Count == 0; attempt++)
            {
                bool folds = attempt == 0;
                var wing = new GraphWing(this);
                var ids = new List<int> { WingSolver.Anchor };
                var hosts = new List<int>();
                for (int index = 0; index < rooms; index++)
                {
                    // Open the clump from a big room, end it in a small one, and join the breaker to the deepest room
                    int room = AddOffice(wing, index == 0 ? BigOfficeRooms : index == rooms - 1 && rooms >= 3 ? SmallRooms : OfficeRooms);
                    int host = index == 0 ? WingSolver.Anchor : isKey && index == rooms - 1 ? ids[^1] : ids[Random.Range(Math.Max(1, ids.Count - 2), ids.Count)];
                    wing.Join(host, room, index == 0 ? via : JoinVia.Door, index == 0 ? hallMark : JoinMark.Junction);
                    if (index == 0)
                    {
                        wing.SetSides(host, room, side, null);
                    }
                    else
                    {
                        wing.Snug(host, room);
                        // Fold the clump, each room past the second but the breaker also touching the other of the last two
                        int? fold = folds && index >= 2 && index < rooms - 1 ? host == ids[^1] ? ids[^2] : ids[^1] : null;
                        if (fold is not null)
                        {
                            wing.Touch(fold.Value, room);
                        }

                        foreach (int other in ids.Skip(1).Where(other => other != host && other != fold))
                        {
                            wing.MayTouch(other, room);
                        }
                    }

                    ids.Add(room);
                    hosts.Add(host);
                }

                var map = wing.Lay(hub, [EndStrip(hub)], folds ? 1 : 2);
                if (map.Count == 0)
                {
                    Count(folds ? BuildingTally.OfficeFoldUnsolved : BuildingTally.OfficeClumpUnsolved);
                    continue;
                }

                built.AddRange(ids.Skip(1).Select(id => map[id]));
                open.UnionWith(ids.Skip(1).Zip(hosts, (room, host) => new RoomPair(map[host], map[room])));
            }

            if (built.Count == 0)
            {
                return [];
            }
        }

        CloseWing(built, open, Draft.WingPods.Concat(built).ToHashSet());
        return built;

        int AddOffice(GraphWing wing, RoomShape[] templates) => wing.AddRoom(DrawType(FrontTypes, RoomType.Office), phase, group, templates);
    }

    protected (List<int> Rooms, List<int> Nested) BuildDepotLoop(int from, int slots, int phase, int nestedPhase, int nested, LabWing kind)
    {
        slots = Math.Min(slots, 11);
        var baseRect = RectOf(from);
        DepotWingLayout? layout = null;
        Func<Cell, Cell>? place = null;
        for (int attempt = 0; attempt < 6 && layout is null; attempt++)
        {
            var drawnKind = attempt < 4 ? kind : LabWing.DepotSections;
            var drawn = new DepotWing(Random).Draw(drawnKind, slots, nested, baseRect.H);
            place = drawn is null ? null : Place(drawn, baseRect, from);
            Count(drawn is null ? BuildingTally.DepotWingUndrawn : place is null ? BuildingTally.DepotWingCollides : BuildingTally.DepotWing);
            if (drawn is not null && place is not null)
            {
                Count(BuildingTally.DepotWing, drawn.Kind);
            }
            layout = place is null ? null : drawn;
        }

        if (layout is null || place is null)
        {
            Count(BuildingTally.DepotWingUnplaced);
            return ([], []);
        }

        var ids = new Dictionary<int, int>();
        var open = new HashSet<RoomPair>();
        var nestedRooms = new List<int>();
        var groups = new Dictionary<int, int> { [0] = NewGroup(), [1] = NewGroup() };
        var order = layout.Run
            .Concat(layout.Rooms.Select(room => room.Id).Where(id => !layout.Run.Contains(id)))
            .Select(id => layout.Rooms[id])
            .ToArray();

        foreach (var room in order)
        {
            bool isNested = room.Role is not (DepotRole.Entry or DepotRole.Run or DepotRole.Passage or DepotRole.Breaker);
            int roomPhase = isNested ? nestedPhase : phase;
            int group = room.Section >= 0 ? groups[room.Section] : NewGroup();
            var type = room.Role switch
            {
                DepotRole.Entry => RoomType.Depot,
                DepotRole.Run => room.Id == layout.Run[1] ? RoomType.Depot : DrawType(DepotTypes, RoomType.Depot),
                DepotRole.Passage or DepotRole.Link or DepotRole.Corridor => RoomType.Passage,
                DepotRole.Breaker => RoomType.MachineRoom,
                DepotRole.Office => RoomType.Office,
                _ => RoomType.Storage
            };

            var neck = room.Neck is null ? [] : room.Neck.Value.Units.ToHashSet();
            var body = room.Cells.Where(cell => !neck.Contains(cell)).Select(place).ToHashSet();
            // Grow the entry strip out of the gate's passage into one entrance depot
            bool joinsGate = room.Id == layout.Run[0] && Grid[from].IsJoint && room.Neck is null;
            int id = joinsGate ? from : AddDepot(body.ToRects(), type, roomPhase, group);
            if (joinsGate)
            {
                foreach (var rect in body.ToRects())
                {
                    AddArm(from, rect, rect.W >= rect.H);
                }

                Grid[from].Type = RoomType.Depot;
                Draft.KeepsOutline.Add(from);
            }

            ids[room.Id] = id;
            if (room.Neck is not null)
            {
                var neckRect = neck.Select(place).ToHashSet().ToRects()[0];
                Grid.Add(neckRect, GridKind.Neck, group, roomPhase, owner: id);
                Draft.Shapes[id].Add(neckRect);
            }

            if (isNested)
            {
                nestedRooms.Add(id);
            }
        }

        var doors = layout.Doors
            .Select(pair => (ids[pair.A], ids[pair.B]))
            .Prepend((from, ids[layout.Run[0]]))
            .Where(pair => pair.Item1 != pair.Item2)
            .ToArray();

        foreach ((int roomA, int roomB) in doors)
        {
            AddDesignedDoor(roomA, roomB, open);
        }

        foreach ((int a, int b) in layout.Shortcuts)
        {
            open.Add(new RoomPair(ids[a], ids[b]));
            Shortcuts.Add(new RoomPair(ids[a], ids[b]));
        }

        JammedWalls.AddRange(layout.Jammed.Select(pair => new RoomPair(ids[pair.A], ids[pair.B])));
        CloseWing(ids.Values.ToArray(), open, new HashSet<int>());
        var run = layout.Run
            .Select(id => ids[id])
            .ToList();

        Run = run;
        return (run.ToList(), nestedRooms);
    }

    private List<int> BuildResearchWing(int joint, int rooms, int phase)
    {
        var baseRect = RectOf(joint);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var layout = new ResearchWing(Random).Draw(rooms);
            var map = layout is null ? null : PlaceResearch(layout, baseRect, joint);
            Count(layout is null ? BuildingTally.ResearchUndrawn : map is null ? BuildingTally.ResearchCollides : BuildingTally.ResearchWing);
            if (layout is null || map is null)
            {
                continue;
            }

            int group = NewGroup();
            var ids = layout.Pieces
                .Select(piece => piece.Part == ResearchPart.Hall
                    ? AddHall(piece.Cells.Select(map).ToHashSet().ToRects()[0], phase, false)
                    : AddDepot(piece.Cells.Select(map).ToHashSet().ToRects(), piece.Type, phase, group))
                .ToList();

            var open = new HashSet<RoomPair> { new(joint, ids[0]) };
            Junctions.Add((joint, ids[0]));
            foreach ((int a, int b) in layout.Doors)
            {
                open.Add(new RoomPair(ids[a], ids[b]));
                Junctions.Add((ids[a], ids[b]));
            }

            JammedWalls.AddRange(layout.Jammed.Select(pair => new RoomPair(ids[pair.A], ids[pair.B])));
            CloseWing(ids, open, new HashSet<int>());
            End = ids[layout.End];
            return ids.Where((_, index) => index > 0 && !layout.Pieces[index].IsPassage).ToList();
        }

        return [];
    }

    // Places a drawn research wing with its hall spanning the joint's far end, nearest the middle first, either way up
    private Func<Cell, Cell>? PlaceResearch(ResearchLayout layout, UnitRect baseRect, int joint)
    {
        int foot = layout.Pieces[0].Cells.Max(cell => cell.V) + 1;
        foreach (int shift in Enumerable.Range(0, foot / 5 + 1).SelectMany(step => new[] { step, -step }).Distinct())
        {
            int top = baseRect.V + (baseRect.H - foot) / 2 + shift;
            foreach (bool mirrored in Random.Shuffled([false, true]))
            {
                var rects = layout.Pieces.SelectMany(piece => piece.Cells.Select(Map).ToHashSet().ToRects());
                if (IsClearOfBuilding(rects, joint))
                {
                    return Map;
                }

                continue;

                Cell Map(Cell cell) => new(baseRect.U + baseRect.W + cell.U, top + (mirrored ? foot - 1 - cell.V : cell.V));
            }
        }

        return null;
    }

    // Returns where a drawn wing stands past the base room's +u end clear of the building, lengthening its entry strip when nothing nearer fits, or
    // null
    private Func<Cell, Cell>? Place(DepotWingLayout layout, UnitRect baseRect, int from)
    {
        var entry = layout.Rooms[layout.Run[0]];
        int low = entry.Cells.Min(cell => cell.U);
        int width = entry.Cells.Max(cell => cell.U) - low + 1;
        int middle = (baseRect.H - width) / 2;
        int[] offsets = Enumerable.Range(0, Math.Max(1, baseRect.H - width + 1))
            .OrderBy(offset => Math.Abs(offset - middle))
            .ToArray();

        foreach (int extra in new[] { 0, 3, 6, 9, 12, 15, 18 })
        {
            var strip = entry.Cells.Concat(new UnitRect(low, -extra, width, extra).ToCells()).ToHashSet();
            var rects = layout.Rooms
                .SelectMany(room => (room.Id == entry.Id ? strip : room.Cells).ToRects())
                .ToArray();

            foreach (int offset in offsets)
            {
                foreach (bool mirrored in Random.Shuffled([false, true]))
                {
                    var placed = rects.Select(rect => Transformed(rect, Map)).ToArray();
                    if (IsClearOfBuilding(placed, from))
                    {
                        entry.Cells = strip;
                        return Map;
                    }

                    continue;

                    Cell Map(Cell cell) => new(baseRect.U + baseRect.W + extra + cell.V,
                        baseRect.V + offset + (mirrored ? low + width - 1 - cell.U : cell.U - low));
                }
            }
        }

        return null;
    }

    private static UnitRect Transformed(UnitRect rect, Func<Cell, Cell> map)
    {
        var a = map(new Cell(rect.U, rect.V));
        var b = map(new Cell(rect.U + rect.W - 1, rect.V + rect.H - 1));
        return new UnitRect(Math.Min(a.U, b.U), Math.Min(a.V, b.V), Math.Abs(a.U - b.U) + 1, Math.Abs(a.V - b.V) + 1);
    }

    private UnitRect[] GetObstacles(int from) => Grid.Rooms
        .Where(room => room.Id != from && room.Owner != from)
        .Select(room => room.Rect)
        .ToArray();

    private bool IsClearOfBuilding(IEnumerable<UnitRect> rects, int from)
    {
        foreach (var rect in rects)
        {
            for (int u = rect.U - 2; u < rect.U + rect.W + 2; u++)
            {
                for (int v = rect.V - 2; v < rect.V + rect.H + 2; v++)
                {
                    int? owner = Grid.GetOwner(new Cell(u, v));
                    if (owner is not null && owner.Value != from && Grid[owner.Value].Owner != from)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    // Adds a depot from its rects, the largest as its body and the rest as arms
    private int AddDepot(UnitRect[] rects, RoomType type, int phase, int group)
    {
        var ordered = rects
            .OrderByDescending(rect => rect.W * rect.H)
            .ToArray();

        int id = AddRoom(ordered[0], type, phase, group: group);
        foreach (var arm in ordered.Skip(1))
        {
            AddArm(id, arm, arm.W >= arm.H);
        }

        Draft.KeepsOutline.Add(id);
        return id;
    }

    // Opens a designed door, a junction within a phase or a lock from the lower phase into the higher, and returns whether it is a junction
    private bool AddDesignedDoor(int a, int b, HashSet<RoomPair> open)
    {
        open.Add(new RoomPair(a, b));
        if (Grid[a].Phase != Grid[b].Phase)
        {
            Locks.Add(Grid[a].Phase < Grid[b].Phase ? (a, b) : (b, a));
            return false;
        }

        Junctions.Add((a, b));
        return true;
    }

    private void CloseWing(IReadOnlyList<int> rooms, HashSet<RoomPair> open, IReadOnlySet<int> kept)
    {
        var members = rooms.ToHashSet();
        var others = Grid.Rooms
            .Where(other => other.Kind is GridKind.Room or GridKind.Hall)
            .ToArray();

        foreach (int room in rooms)
        {
            foreach (var other in others.Where(other => other.Id != room && (!members.Contains(other.Id) || other.Id > room)))
            {
                bool touches = Shapes(room).Any(rect => Shapes(other.Id).Any(part => rect.SharesWall(part)));
                bool isKept = kept.Contains(room) && kept.Contains(other.Id) && other.Kind == GridKind.Room;
                if (touches && !isKept && !open.Contains(new RoomPair(room, other.Id)))
                {
                    ClosedWalls.Add(new RoomPair(room, other.Id));
                }
            }
        }
    }

    protected (int Hub, int Joint) AddHub(int? jointPhase, int length)
    {
        int width = length >= 25 ? Random.Choice((int[])[6, 8]) : 6;
        int hub = AddHall(new UnitRect(0, 0, length, width), 0, true);
        var hubRect = RectOf(hub);
        int joint = jointPhase is null ? hub : AddRoom(hubRect.Beside(Side.East, 4, 4, hubRect.V + (width - 4) / 2), RoomType.Passage, jointPhase.Value, isJoint: true);
        return (hub, joint);
    }

    protected int AddHall(UnitRect rect, int phase, bool alongU)
    {
        int id = Grid.Add(rect, GridKind.Hall, 0, phase, alongU: alongU);
        Draft.Shapes[id] = [rect];
        return id;
    }

    protected int AddArm(int owner, UnitRect rect, bool alongU) => Draft.AddArm(owner, rect, alongU);

    protected int AddRoom(UnitRect rect, RoomType type, int phase, bool isJoint = false, int group = 0)
    {
        int id = Grid.Add(rect, GridKind.Room, group == 0 ? NewGroup() : group, phase, type, isJoint: isJoint);
        Draft.Shapes[id] = [rect];
        return id;
    }

    protected int AddEndRoom(int hall, Side end, int phase, bool viaNeck)
    {
        var rect = RectOf(hall);
        int along = Math.Max(viaNeck ? 5 : 4, Size(0.9f, 1.1f));
        (int low, int high) = rect.GetSpan(end);
        int span = high - low;
        if (!viaNeck)
        {
            return AddRoom(rect.Beside(end, along, Size(0.9f, 1.1f), low + FloorDivide(span - along, 2)), RoomType.Bare, phase);
        }

        var neck = rect.Beside(end, 3, NeckDepth, low + FloorDivide(span - 3, 2));
        int id = AddRoom(neck.Beside(end, along, Size(0.9f, 1.1f), low + FloorDivide(span - along, 2)), RoomType.Bare, phase);
        Grid.Add(neck, GridKind.Neck, Grid[id].Group, phase, owner: id);
        Draft.Shapes[id].Add(neck);
        return id;
    }

    protected UnitRect RectOf(int id) => Draft.RectOf(id);

    protected int NewGroup() => Draft.NewGroup();

    private IReadOnlyList<UnitRect> Shapes(int id) => Draft.GetShapes(id);

    protected Dictionary<int, int> DoorDepths(IReadOnlyList<int> ids) => ids
        .ToDictionary(room => room, room => ids.Where(other => Shapes(room).Any(rect => Shapes(other).Any(part => rect.SharesWall(part)))).ToList())
        .MeasureHops([ids[0]]);

    protected int Size(float low, float high) => Draft.Size(low, high);

    protected RoomType DrawType(IReadOnlyList<(RoomType Type, float Weight)> types, RoomType main) => Draft.DrawType(types, main);

    protected static int FloorDivide(int value, int divisor) => (int)MathF.Floor(value / (float)divisor);
}

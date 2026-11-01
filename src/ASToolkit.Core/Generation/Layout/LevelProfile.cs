using ASToolkit.Core.Rooms;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the rooms of a level by role, with areas in its setting's units.
/// </summary>
public sealed class LevelProfile
{
    private const int MinRoomUnits = 9;
    private static readonly (float Min, float Max) LikeRatio = (0.6f, 1.6f);

    private LevelProfile()
    {
    }

    /// <summary>
    /// Gets the setting the units belong to.
    /// </summary>
    public required ThemeSettings Settings { get; init; }

    /// <summary>
    /// Gets the area of the start room.
    /// </summary>
    public required int StartUnits { get; init; }

    /// <summary>
    /// Gets the length and width of the hub hall and of the second hall on the main body when there is one, or none for a compound whose
    /// start leads straight into its rooms.
    /// </summary>
    public required IReadOnlyList<(int Length, int Width)> Halls { get; init; }

    /// <summary>
    /// Gets the area of the room that leads from the hub to the rest of the main body.
    /// </summary>
    public required int NeckUnits { get; init; }

    /// <summary>
    /// Gets the areas of the rooms the main path passes between the start and the hub, nearest the start first.
    /// </summary>
    public required IReadOnlyList<int> LeadRooms { get; init; }

    /// <summary>
    /// Gets the areas of the main body's other rooms on the hub's sides.
    /// </summary>
    public required IReadOnlyList<int> HubRooms { get; init; }

    /// <summary>
    /// Gets the areas of the main body's rooms on the second hall's sides.
    /// </summary>
    public required IReadOnlyList<int> HallRooms { get; init; }

    /// <summary>
    /// Gets the areas of the main body's rooms that stand together in one block.
    /// </summary>
    public required IReadOnlyList<int> BlockRooms { get; init; }

    /// <summary>
    /// Gets a value indicating whether the main body reaches the block before the second hall.
    /// </summary>
    public required bool IsBlockFirst { get; init; }

    /// <summary>
    /// Gets a value indicating whether the second hall runs across the hub.
    /// </summary>
    public required bool IsSecondHallAcross { get; init; }

    /// <summary>
    /// Gets the depth of the main body's block along the hub's axis over its breadth across it.
    /// </summary>
    public required float BodyDepth { get; init; }

    /// <summary>
    /// Gets where the neck enters the main body's block across its breadth, from 0 at one end to 0.5 in the middle.
    /// </summary>
    public required float BodyEntry { get; init; }

    /// <summary>
    /// Gets a value indicating whether the rooms on the second hall stand apart with gaps between them.
    /// </summary>
    public required bool HasPods { get; init; }

    /// <summary>
    /// Gets the areas of the rooms of each branch rooted on the hub in the order the walk from the hub enters them.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<int>> Branches { get; init; }

    /// <summary>
    /// Gets the number of rooms at the head of each branch that open onto the hub.
    /// </summary>
    public required IReadOnlyList<int> BranchEntrances { get; init; }

    /// <summary>
    /// Gets the areas of the rooms of the branches rooted past the hub, each branch a chain off the main body from its root out.
    /// </summary>
    public required IReadOnlyList<IReadOnlyList<int>> DeadEnds { get; init; }

    /// <summary>
    /// Gets the areas of the furnished rooms behind breakable walls.
    /// </summary>
    public required IReadOnlyList<int> SecretRooms { get; init; }

    /// <summary>
    /// Gets the area of the room the breaker stands in.
    /// </summary>
    public required int KeyUnits { get; init; }

    /// <summary>
    /// Gets the role of the first room behind the fences, or <see langword="null"/> for a level without fences.
    /// </summary>
    public required RoomRole? FenceRole { get; init; }

    /// <summary>
    /// Gets a value indicating whether the key room stands apart from the branches.
    /// </summary>
    public required bool IsKeyApart { get; init; }

    /// <summary>
    /// Gets the area of each room off the halls with the share of its bounding box its floor fills.
    /// </summary>
    public required IReadOnlyList<(int Units, float Fill)> Shapes { get; init; }

    /// <summary>
    /// Gets the floor area of every room.
    /// </summary>
    public int Units => StartUnits + LeadRooms.Sum() + Halls.Sum(hall => hall.Length * hall.Width) + NeckUnits + HubRooms.Sum() + HallRooms.Sum() + BlockRooms.Sum()
        + Branches.Sum(branch => branch.Sum()) + DeadEnds.Sum(chain => chain.Sum());

    /// <summary>
    /// Measures the rooms of a surveyed level in a setting's units.
    /// </summary>
    public static LevelProfile Measure(LevelSurvey survey, ThemeSettings settings)
    {
        float unitSide = settings.UnitPixels;
        var places = survey.Places.ToDictionary(place => place.Id);
        var roles = RoomRoleMap.Measure(survey);
        int hub = roles.Hub;
        var path = roles.Path.ToList();
        int[] lead = roles.HasHall ? path.Skip(1).Take(path.IndexOf(hub) - 1).ToArray() : [];
        var branches = roles.Branches.Where(branch => branch is not [var only] || only != roles.Key).ToArray();
        var hubBranches = branches.Where(branch => roles.AreJoined(branch[0], hub)).OrderByDescending(branch => branch.Count).ToArray();
        int ToUnits(int room) => Math.Max(MinRoomUnits, (int)MathF.Round(places[room].AreaPixels / settings.UnitArea));
        (int, float) ToShape(LevelPlace place) =>
            (ToUnits(place.Id), place.Cells.Count / (float)((place.Cells.Max(c => c.U) - place.Cells.Min(c => c.U) + 1) * (place.Cells.Max(c => c.V) - place.Cells.Min(c => c.V) + 1)));
        (int, int) ToSize(int hall) => ((int)MathF.Round(places[hall].LengthPixels / unitSide), (int)MathF.Round(places[hall].WidthPixels / unitSide));
        return new LevelProfile
        {
            Settings = settings,
            StartUnits = ToUnits(roles.Start),
            Halls = !roles.HasHall ? [] : roles.SecondHall is null ? [ToSize(hub)] : [ToSize(hub), ToSize(roles.SecondHall.Value)],
            NeckUnits = ToUnits(roles.Neck),
            LeadRooms = lead.Select(ToUnits).ToArray(),
            HubRooms = roles.HubRooms.Except(lead).Select(ToUnits).ToArray(),
            HallRooms = roles.HallRooms.Select(ToUnits).ToArray(),
            BlockRooms = roles.BodyRooms.Except(lead).Select(ToUnits).ToArray(),
            IsBlockFirst = roles.SecondHall is not null && path.IndexOf(roles.SecondHall.Value) > path.IndexOf(roles.Neck) + 1,
            IsSecondHallAcross = roles.SecondHall is not null && IsAlongU(places[hub]) != IsAlongU(places[roles.SecondHall.Value]),
            BodyDepth = MeasureBodyDepth(roles.BodyRooms.Except(lead).Select(room => places[room]).ToArray(), IsAlongU(places[hub])),
            BodyEntry = MeasureBodyEntry(roles.BodyRooms.Except(lead).Select(room => places[room]).ToArray(), places[roles.Neck], IsAlongU(places[hub])),
            HasPods = survey.Style == LevelStyle.Ungated,
            Branches = hubBranches.Select(branch => branch.Select(ToUnits).ToArray()).ToArray(),
            BranchEntrances = hubBranches.Select(branch => branch.Count(room => roles.AreJoined(room, hub))).ToArray(),
            DeadEnds = branches.Where(branch => !roles.AreJoined(branch[0], hub)).Select(branch => branch.Select(ToUnits).ToArray()).ToArray(),
            SecretRooms = survey.Places.Where(place => place.Kind == PlaceKind.Room && roles.GetRole(place) == RoomRole.Secret).Select(place => ToUnits(place.Id)).ToArray(),
            IsKeyApart = roles.Key is not null && !branches.Any(branch => branch.Contains(roles.Key.Value)),
            FenceRole = survey.Connections.Where(connection => connection.Kind == ConnectionKind.Fence)
                .SelectMany(connection => new[] { connection.A, connection.B })
                .Where(id => places.TryGetValue(id, out var place) && place is { Zone: GateZone.Fenced, Kind: PlaceKind.Room })
                .Select(id => (RoomRole?)roles.GetRole(places[id]))
                .FirstOrDefault(),
            KeyUnits = roles.Key is null ? branches.Where(branch => branch.Count == 1).Select(branch => ToUnits(branch[0])).DefaultIfEmpty(MinRoomUnits).Min() : ToUnits(roles.Key.Value),
            Shapes = survey.Places.Where(place => place.Kind == PlaceKind.Room).Select(ToShape).ToArray()
        };
    }

    /// <summary>
    /// Draws a profile with room counts within one of this profile's and areas from the rooms of the same role in a pool, scaled to this
    /// profile's floor by role and to a floor area, with its largest branch grown to a share of the floor when one is given.
    /// </summary>
    public LevelProfile Sample(Random rng, IReadOnlyList<LevelProfile> pool, int units, float wingShare = 0)
    {
        float scale = units / (float)Units;
        int[] rootPool = Collect(pool.SelectMany(profile => profile.Branches.Select(branch => branch[0])));
        int[] tailPool = Collect(pool.SelectMany(profile => profile.Branches.SelectMany(branch => branch.Skip(1))));
        var shapes = Branches.Select((branch, index) => (Rooms: branch.Count, Entrances: BranchEntrances[index])).OrderBy(_ => rng.Next()).ToArray();
        var picked = shapes.OrderByDescending(shape => shape.Rooms).ToList();
        int change = shapes.Length == 0 ? 0 : rng.Next(-1, 2);
        if (change < 0 && picked.Count > 1)
        {
            picked.RemoveAt(picked.Count - 1);
        }
        else if (change > 0)
        {
            picked.Add(shapes[rng.Next(shapes.Length)]);
        }

        int[][] drawn = picked
            .Select(shape => Enumerable.Range(0, shape.Rooms).Select(index => index < shape.Entrances ? rootPool[rng.Next(rootPool.Length)] : tailPool[rng.Next(tailPool.Length)]).ToArray())
            .ToArray();
        float branchScale = Branches.Sum(branch => branch.Sum()) * scale / Math.Max(1, drawn.Sum(branch => branch.Sum()));
        var profile = new LevelProfile
        {
            Settings = Settings,
            StartUnits = StartUnits,
            Halls = Halls.Select(hall => (Math.Max(hall.Width, (int)MathF.Round(hall.Length * scale)), hall.Width)).ToArray(),
            NeckUnits = Math.Max(MinRoomUnits, (int)MathF.Round(NeckUnits * scale)),
            LeadRooms = LeadRooms.Select(area => Math.Max(MinRoomUnits, (int)MathF.Round(area * scale))).ToArray(),
            HubRooms = Draw(rng, pool.SelectMany(profile => profile.HubRooms), HubRooms, scale),
            HallRooms = Draw(rng, pool.SelectMany(profile => profile.HallRooms), HallRooms, scale),
            BlockRooms = Draw(rng, pool.SelectMany(profile => profile.BlockRooms), BlockRooms, scale),
            IsBlockFirst = IsBlockFirst,
            IsSecondHallAcross = IsSecondHallAcross,
            BodyDepth = BodyDepth,
            BodyEntry = BodyEntry,
            HasPods = HasPods,
            Branches = drawn.Select(branch => branch.Select(area => Math.Max(MinRoomUnits, (int)MathF.Round(area * branchScale))).ToArray()).ToArray(),
            BranchEntrances = picked.Select(shape => shape.Entrances).ToArray(),
            DeadEnds = DrawChains(rng, pool.SelectMany(profile => profile.DeadEnds), DeadEnds, scale),
            KeyUnits = KeyUnits,
            SecretRooms = SecretRooms,
            IsKeyApart = IsKeyApart,
            FenceRole = FenceRole,
            Shapes = pool.SelectMany(profile => profile.Shapes).ToArray()
        };
        return wingShare > 0 && profile.Branches.Count > 0 ? profile.GrowWing(wingShare) : profile;
    }

    /// <summary>
    /// Returns a copy with each room grown to the box an original room of a like size fills on average.
    /// </summary>
    public LevelProfile Inflate()
    {
        int Grow(int area)
        {
            var like = Shapes.Where(shape => shape.Units >= area * LikeRatio.Min && shape.Units <= area * LikeRatio.Max).ToArray();
            return like.Length == 0 ? area : (int)MathF.Round(area / like.Average(shape => shape.Fill));
        }

        int[] GrowAll(IEnumerable<int> areas) => areas.Select(Grow).ToArray();
        return new LevelProfile
        {
            Settings = Settings,
            StartUnits = StartUnits,
            Halls = Halls,
            NeckUnits = Grow(NeckUnits),
            LeadRooms = GrowAll(LeadRooms),
            HubRooms = GrowAll(HubRooms),
            HallRooms = GrowAll(HallRooms),
            BlockRooms = GrowAll(BlockRooms),
            IsBlockFirst = IsBlockFirst,
            IsSecondHallAcross = IsSecondHallAcross,
            BodyDepth = BodyDepth,
            BodyEntry = BodyEntry,
            HasPods = HasPods,
            Branches = Branches.Select(GrowAll).ToArray(),
            BranchEntrances = BranchEntrances,
            DeadEnds = DeadEnds.Select(GrowAll).ToArray(),
            KeyUnits = KeyUnits,
            SecretRooms = SecretRooms,
            IsKeyApart = IsKeyApart,
            FenceRole = FenceRole,
            Shapes = Shapes
        };
    }

    // Grow the largest branch to a share of the floor and shrink the other rooms off the halls to keep the floor
    private LevelProfile GrowWing(float share)
    {
        int wing = Enumerable.Range(0, Branches.Count).MaxBy(index => Branches[index].Sum());
        int rest = Units - StartUnits - KeyUnits - Halls.Sum(hall => hall.Length * hall.Width);
        float grow = share * Units / Branches[wing].Sum();
        float shrink = Math.Max(0.2f, (rest - share * Units) / Math.Max(1, rest - Branches[wing].Sum()));
        int[] Scale(IEnumerable<int> areas, float factor) => areas.Select(area => Math.Max(MinRoomUnits, (int)MathF.Round(area * factor))).ToArray();
        return new LevelProfile
        {
            Settings = Settings,
            StartUnits = StartUnits,
            Halls = Halls,
            NeckUnits = Scale([NeckUnits], shrink)[0],
            LeadRooms = Scale(LeadRooms, shrink),
            HubRooms = Scale(HubRooms, shrink),
            HallRooms = Scale(HallRooms, shrink),
            BlockRooms = Scale(BlockRooms, shrink),
            IsBlockFirst = IsBlockFirst,
            IsSecondHallAcross = IsSecondHallAcross,
            BodyDepth = BodyDepth,
            BodyEntry = BodyEntry,
            HasPods = HasPods,
            Branches = Branches.Select((branch, index) => Scale(branch, index == wing ? grow : shrink)).ToArray(),
            BranchEntrances = BranchEntrances,
            DeadEnds = DeadEnds.Select(chain => Scale(chain, shrink)).ToArray(),
            KeyUnits = KeyUnits,
            SecretRooms = SecretRooms,
            IsKeyApart = IsKeyApart,
            FenceRole = FenceRole,
            Shapes = Shapes
        };
    }

    private static int[] Collect(IEnumerable<int> areas) => areas.DefaultIfEmpty(MinRoomUnits).ToArray();

    private static int Jitter(Random rng, int count) => count == 0 ? 0 : Math.Max(1, count + rng.Next(-1, 2));

    // Draw a count near the own count from the pool and scale the areas to the own total
    private static float MeasureBodyDepth(LevelPlace[] block, bool isHubAlongU)
    {
        var cells = block.SelectMany(place => place.Cells).ToArray();
        if (cells.Length == 0)
        {
            return 1;
        }

        float alongU = cells.Max(c => c.U) - cells.Min(c => c.U) + 1;
        float alongV = cells.Max(c => c.V) - cells.Min(c => c.V) + 1;
        return isHubAlongU ? alongU / alongV : alongV / alongU;
    }

    private static float MeasureBodyEntry(LevelPlace[] block, LevelPlace neck, bool isHubAlongU)
    {
        int[] across = block.SelectMany(place => place.Cells).Select(c => isHubAlongU ? c.V : c.U).ToArray();
        if (across.Length == 0)
        {
            return 0.5f;
        }

        float entry = ((float)neck.Cells.Average(c => isHubAlongU ? c.V : c.U) - across.Min()) / (across.Max() - across.Min() + 1);
        return Math.Clamp(Math.Min(entry, 1 - entry), 0, 0.5f);
    }

    private static bool IsAlongU(LevelPlace place) => place.Cells.Max(c => c.U) - place.Cells.Min(c => c.U) >= place.Cells.Max(c => c.V) - place.Cells.Min(c => c.V);

    private static IReadOnlyList<IReadOnlyList<int>> DrawChains(Random rng, IEnumerable<IReadOnlyList<int>> pool, IReadOnlyList<IReadOnlyList<int>> own, float scale)
    {
        var options = pool.ToArray();
        if (options.Length == 0)
        {
            return [];
        }

        var chains = Enumerable.Range(0, Jitter(rng, own.Count)).Select(_ => options[rng.Next(options.Length)]).ToArray();
        float factor = own.Sum(chain => chain.Sum()) * scale / Math.Max(1, chains.Sum(chain => chain.Sum()));
        return chains.Select(chain => chain.Select(area => Math.Max(MinRoomUnits, (int)MathF.Round(area * factor))).ToArray()).OrderBy(chain => chain.Sum()).ToArray();
    }

    private static int[] Draw(Random rng, IEnumerable<int> pool, IReadOnlyList<int> own, float scale)
    {
        int[] options = Collect(pool);
        int[] areas = Enumerable.Range(0, Jitter(rng, own.Count)).Select(_ => options[rng.Next(options.Length)]).ToArray();
        float factor = own.Sum() * scale / Math.Max(1, areas.Sum());
        return areas.Select(area => Math.Max(MinRoomUnits, (int)MathF.Round(area * factor))).Order().ToArray();
    }
}

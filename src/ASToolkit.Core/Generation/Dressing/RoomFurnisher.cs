using System.Numerics;
using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Dressing;

/// <summary>
/// Types the rooms of a generated level from the original's rooms of the same role and furnishes each from its type's recipe.
/// </summary>
public sealed class RoomFurnisher
{
    private const float AreaUnit = 100_000;
    private const float Near = 60;
    private const float Tolerance = 12;
    private const int SpotTries = 30;
    private const int ExampleTries = 8;
    private const int MaxMisses = 6;
    private const float OwnShare = 0.75f;
    private const int MaxPartsPerSource = 2;
    private const int MinTrackedPieces = 3;
    private const float SizeJitter = 0.3f;
    private const int MinPieces = 3;
    private static readonly (int U, int V)[] Axes = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly float MaxSizeLog = MathF.Log(3);
    private const int SourceLevel = -2;
    private const float FloorHeight = 5;
    private const float ClearanceSlack = 0.05f;
    private static readonly (float Min, float Max) DensityJitter = (0.8f, 1.2f);

    private readonly Dictionary<RoomType, (RoomRecipe Recipe, RecipePart[] Parts)> _recipes;
    private readonly ObjectTable _objects;
    private readonly Func<KnownObject, float, bool> _allows;
    private readonly Func<KnownObject, float> _heightOf;
    private readonly Lattice _lattice;
    private readonly Dictionary<KnownObject, float> _spans;

    /// <summary>
    /// Initializes a furnisher with the recipes of a setting, keeping only the parts whose pieces the level may hold.
    /// </summary>
    /// <param name="recipes">The recipes of the setting's room types.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="allows">Returns whether the level may hold an object at a height.</param>
    /// <param name="heightOf">Returns the height to lay a scattered object at.</param>
    /// <param name="lattice">A lattice with the axes of the rooms' walls.</param>
    public RoomFurnisher(IEnumerable<RoomRecipe> recipes, ObjectTable objects, Func<KnownObject, float, bool> allows, Func<KnownObject, float> heightOf, Lattice lattice)
    {
        _objects = objects;
        _heightOf = heightOf;
        _allows = allows;
        _lattice = lattice;
        var measured = recipes.ToArray();
        _spans = MeasureSpans(measured);
        _recipes = Prepare(measured);
    }

    /// <summary>
    /// Types the rooms of a generated level from the original's rooms of the same role and furnishes each from its type's recipe.
    /// </summary>
    /// <param name="rng">The random source.</param>
    /// <param name="plan">The layout plan.</param>
    /// <param name="layout">The fine layout.</param>
    /// <param name="original">The rooms of the original level.</param>
    /// <param name="keepClear">The cells to keep free of solid pieces.</param>
    /// <param name="wallLattice">The lattice the built walls are read on.</param>
    /// <param name="walls">The edges of the walls as built.</param>
    /// <param name="facings">How the originals turn pieces against walls.</param>
    /// <param name="clearance">How close the originals stand pieces to walls.</param>
    public (Dictionary<int, RoomType> Types, List<PlacedDecor> Decor) Furnish(Random rng, LayoutPlan plan, FineLayout layout, RoomAtlas original,
        IReadOnlySet<Cell> keepClear, Lattice wallLattice, IReadOnlyDictionary<EdgeKey, EdgeKind> walls, FacingTable facings, WallClearanceTable clearance)
    {
        var own = Prepare(RoomRecipe.Measure(original.Rooms, layout.Lattice));
        var types = AssignTypes(rng, plan, layout, original);
        var standing = new Standing(wallLattice, walls, facings, clearance);
        var densities = original.Rooms.Where(room => IsFurnished(room) && room.Side is null).GroupBy(room => room.Type)
            .ToDictionary(group => group.Key, group => group.Sum(room => room.Parts.Sum(part => part.Pieces.Count)) / (group.Sum(room => room.Place.AreaPixels) / AreaUnit));
        var placed = new List<PlacedDecor>();
        var used = new HashSet<RoomPart>();
        float cellArea = 2 * layout.Lattice.StepX * layout.Lattice.StepY;
        float floorRatio = types.Keys.Sum(room => layout.GetRegionCells(room).Count) * cellArea / Math.Max(1, original.Rooms.Where(IsFurnished).Sum(room => room.Place.AreaPixels));
        var caps = original.Rooms.Where(IsFurnished).SelectMany(room => room.Parts).SelectMany(part => part.Pieces).GroupBy(piece => piece.Id)
            .ToDictionary(group => group.Key, group => (int)MathF.Ceiling(group.Count() * floorRatio));
        var counts = new Dictionary<KnownObject, int>();
        var rooms = new List<Room>();
        foreach ((int id, var type) in types.OrderBy(pair => pair.Key))
        {
            if (!_recipes.TryGetValue(type, out var pooled) || pooled.Parts.Length == 0)
            {
                continue;
            }

            var (recipe, parts) = own.TryGetValue(type, out var level) && level.Parts.Length > 0 ? level : pooled;
            var cells = layout.GetRegionCells(id).ToHashSet();
            float units = cells.Count * cellArea / AreaUnit;
            float density = densities.TryGetValue(type, out float measured) ? measured : parts.Sum(part => part.Rate * (float)part.Examples.Average(example => example.Pieces.Count));
            int wanted = Math.Max(MinPieces, (int)MathF.Round(density * units * (DensityJitter.Min + (DensityJitter.Max - DensityJitter.Min) * rng.NextSingle())));
            var picks = parts.Where(part => part.IsRequired).OrderByDescending(part => part.Rate).Select(part => (Part: part, Example: Pick(rng, part))).ToList();
            var drawn = new List<(RecipePart Part, RoomPart Example)>();
            for (int expected = picks.Sum(pick => pick.Example.Pieces.Count); expected < wanted;)
            {
                var part = Draw(rng, rng.NextSingle() < OwnShare ? parts : pooled.Parts);
                drawn.Add((part, Pick(rng, part)));
                expected += drawn[^1].Example.Pieces.Count;
            }

            picks.AddRange(drawn.OrderByDescending(pick => pick.Example.Pieces.Count));
            rooms.Add(new Room(cells, layout.Edges, used, caps, counts)
            {
                Recipe = recipe,
                Parts = parts,
                Pooled = pooled.Parts,
                Units = units,
                Wanted = wanted,
                Picks = new Queue<(RecipePart, RoomPart)>(picks)
            });
        }

        while (rooms.Any(room => !room.IsDone))
        {
            foreach (var room in rooms.Where(room => !room.IsDone))
            {
                if (room.Picks.TryDequeue(out var pick))
                {
                    room.Pieces += TryPlace(rng, layout, keepClear, room, pick.Part, standing, placed, pick.Example);
                    continue;
                }

                bool fromLevel = rng.NextSingle() < OwnShare;
                int count = TryPlace(rng, layout, keepClear, room, Draw(rng, fromLevel ? room.Parts : room.Pooled), standing, placed);
                if (count == 0 && fromLevel)
                {
                    count = TryPlace(rng, layout, keepClear, room, Draw(rng, room.Pooled), standing, placed);
                }

                room.Pieces += count;
                room.Misses = count > 0 ? 0 : room.Misses + 1;
            }
        }

        foreach (var room in rooms)
        {
            Scatter(rng, layout.Lattice, room.Cells, room.Taken, room.Recipe, room.Units, standing, placed);
        }

        return (types, placed);
    }
    private static bool IsFurnished(AtlasRoom room) => room.IsRoom || room.Role == RoomRole.Secret;


    // Give each room the type of the original room of its role nearest it in area and within a factor of 3, each original room once and an
    // empty one only to a room with two doors or more, and then each room left the type of the furnished original room of any role nearest
    // it in area, unused ones within a factor of 3 first
    private static Dictionary<int, RoomType> AssignTypes(Random rng, LayoutPlan plan, FineLayout layout, RoomAtlas original)
    {
        float cellArea = 2 * layout.Lattice.StepX * layout.Lattice.StepY;
        
        var rooms = plan.Regions
            .Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing or RegionKind.SecretRoom)
            .ToDictionary(region => region.Id);
        
        var roles = plan.MeasureRoles();
        
        var known = original.Rooms
            .Where(room => IsFurnished(room) && room.Type != RoomType.Hall)
            .ToArray();
        
        var unused = original.Rooms
            .Where(room => IsFurnished(room) && (room.Type != RoomType.Hall || room.Role == RoomRole.HallRow))
            .ToList();
        
        var keyType = known.FirstOrDefault(room => room.Role == RoomRole.Key || room.Place.Use == Survey.PlaceUse.BreakerRoom)?.Type;
        var types = new Dictionary<int, RoomType>();
        var left = new List<int>();
        foreach (var region in rooms.Values.OrderBy(_ => rng.Next()))
        {
            if (region.IsHall)
            {
                types[region.Id] = RoomType.Hall;
                continue;
            }

            if (region.Id == plan.KeyRoom?.Id && keyType is not null)
            {
                types[region.Id] = keyType.Value;
                continue;
            }

            int area = Area(region.Id);
            var role = region.Kind == RegionKind.SecretRoom ? RoomRole.Secret : roles.GetRole(region.Id, false);
            bool connects = roles.CountDoors(region.Id) >= 2;
            
            var match = unused
                .Where(room => room.Role == role && (connects || !IsEmpty(room.Type)) && Fits(room, area))
                .MinBy(room => Distance(room, area));
            
            if (match is null)
            {
                left.Add(region.Id);
                continue;
            }

            types[region.Id] = match.Type;
            unused.Remove(match);
        }

        foreach (int id in left)
        {
            int area = Area(id);
            var match = unused 
                            .Where(room => !IsEmpty(room.Type) && room.Type != RoomType.Hall && Fits(room, area))
                            .MinBy(room => Distance(room, area))
                            ?? known 
                            .Where(room => !IsEmpty(room.Type))
                            .MinBy(room => Distance(room, area));
            
            types[id] = match?.Type ?? RoomType.Passage;
            if (match is not null)
            {
                unused.Remove(match);
            }
        }

        return types;

        int Area(int room) => (int)(layout.GetRegionCells(room).Count * cellArea);
        
        static bool IsEmpty(RoomType type) => type is RoomType.Passage or RoomType.Bare;

        static bool Fits(AtlasRoom room, int area) => MathF.Abs(MathF.Log(room.Place.AreaPixels / (float)area)) <= MaxSizeLog;

        float Distance(AtlasRoom room, int area) => MathF.Abs(MathF.Log(room.Place.AreaPixels / (float)Math.Max(1, area))) + SizeJitter * rng.NextSingle();
    }

    private Dictionary<RoomType, (RoomRecipe Recipe, RecipePart[] Parts)> Prepare(IEnumerable<RoomRecipe> recipes) =>
        recipes.ToDictionary(recipe => recipe.Type, recipe => (recipe, recipe.Parts
            .Select(part => new RecipePart
            {
                SpotShares = part.SpotShares,
                Families = part.Families,
                Rate = part.Rate,
                RoomShare = part.RoomShare,
                Spacing = part.Spacing,
                RowShare = part.RowShare,
                Examples = part.Examples.Where(example => example.Pieces.All(piece => _allows(piece.Id, piece.Z))).ToArray()
            })
            .Where(part => part.Examples.Count > 0)
            .ToArray()));

    // Measure how far from its nearest neighbor of its kind side by side along each lattice axis the originals mostly stand a piece, and
    // keep the wider axis
    private Dictionary<KnownObject, float> MeasureSpans(IEnumerable<RoomRecipe> recipes)
    {
        var gaps = new Dictionary<(KnownObject Id, bool AlongU), List<float>>();
        foreach (var pieces in recipes.SelectMany(recipe => recipe.Parts).SelectMany(part => part.Examples).Select(example => example.Pieces).Distinct())
        {
            foreach (var one in pieces)
            {
                (float u0, float v0) = ToAxes(one.X, one.Y);
                
                var offsets = pieces
                    .Where(two => two.Id == one.Id && MathF.Abs(one.Z - two.Z) < FloorHeight)
                    .Select(two => ToAxes(two.X, two.Y))
                    .Select(at => (U: MathF.Abs(at.U - u0), V: MathF.Abs(at.V - v0)))
                    .ToArray();
                
                foreach (bool alongU in new[] { true, false })
                {
                    float nearest = offsets
                        .Where(gap => (alongU ? gap.V : gap.U) <= Tolerance && (alongU ? gap.U : gap.V) > Tolerance)
                        .Select(gap => alongU ? gap.U : gap.V)
                        .DefaultIfEmpty(0)
                        .Min();
                    
                    if (nearest > 0)
                    {
                        (gaps.TryGetValue((one.Id, alongU), out var list) ? list : gaps[(one.Id, alongU)] = []).Add(nearest);
                    }
                }
            }
        }

        return gaps
            .GroupBy(pair => pair.Key.Id)
            .ToDictionary(group => group.Key, group => group.Max(pair => pair.Value.Order().ElementAt(pair.Value.Count / 2)));
    }

    // Take half the span the originals leave between two of the piece, or else half its collision size, and no more toward a wall than the
    // piece stood from it in its source
    private Footprint GetFootprint(PartPiece piece, float x, float y)
    {
        float half = (_spans.TryGetValue(piece.Id, out float span) ? span : _objects.TryFind(piece.Id, out var definition) ? MathF.Max(definition.Size.X, definition.Size.Y) : 0) / 2;
        (float u, float v) = ToAxes(x, y);
        return new Footprint(u, v, MathF.Min(half, MathF.Min(piece.Gaps.MinusU, piece.Gaps.PlusU)), MathF.Min(half, MathF.Min(piece.Gaps.MinusV, piece.Gaps.PlusV)));
    }

    private (float U, float V) ToAxes(float x, float y)
    {
        var point = _lattice.ToLattice(x, y);
        return (point.U * _lattice.CellStep, point.V * _lattice.CellStep);
    }

    // Draw up to a number of distinct cells
    private static IEnumerable<Cell> Sample(Random rng, Cell[] cells, int count)
    {
        var pool = cells.ToArray();
        for (int index = 0; index < Math.Min(count, pool.Length); index++)
        {
            int pick = rng.Next(index, pool.Length);
            (pool[index], pool[pick]) = (pool[pick], pool[index]);
            yield return pool[index];
        }
    }

    private static RoomPart Pick(Random rng, RecipePart part) => part.Examples[rng.Next(part.Examples.Count)];

    private static RecipePart Draw(Random rng, RecipePart[] parts)
    {
        float pick = rng.NextSingle() * parts.Sum(part => part.Rate);
        foreach (var part in parts)
        {
            pick -= part.Rate;
            if (pick <= 0)
            {
                return part;
            }
        }

        return parts[^1];
    }

    // Copy into the room the given example or else one of the part's examples, never a group of three or more pieces twice in a level or
    // more than twice from one original room into one room, nor past the level's cap of any piece, and return the number of pieces placed
    private int TryPlace(Random rng, FineLayout layout, IReadOnlySet<Cell> keepClear, Room room, RecipePart part, Standing standing, List<PlacedDecor> placed,
        RoomPart? first = null)
    {
        for (int attempt = 0; attempt < ExampleTries; attempt++)
        {
            var example = attempt == 0 && first is not null ? first : Pick(rng, part);
            bool tracked = example.Pieces.Count >= MinTrackedPieces;
            if (tracked && (room.Used.Contains(example) || room.Sources.GetValueOrDefault(example.Room) >= MaxPartsPerSource) || !room.IsWithinCaps(example)
                || !TryPlace(rng, layout, keepClear, room, example, part, standing, placed))
            {
                continue;
            }

            room.Count(example);
            if (tracked)
            {
                room.Used.Add(example);
                room.Sources[example.Room] = room.Sources.GetValueOrDefault(example.Room) + 1;
            }

            return example.Pieces.Count;
        }

        return 0;
    }

    // Stand the part, as often as its kind stands in rows, a spacing away along the walls from one of its kind against the same walls, or
    // else stand the piece against the most walls at its walls' distances in a cell by those walls, then check every piece
    private bool TryPlace(Random rng, FineLayout layout, IReadOnlySet<Cell> keepClear, Room room, RoomPart example, RecipePart kind, Standing standing,
        List<PlacedDecor> placed)
    {
        var lattice = layout.Lattice;
        var pieces = example.Pieces;
        
        int anchor = Enumerable.Range(0, pieces.Count)
            .OrderByDescending(index => Enumerable.Range(0, 4).Count(direction => pieces[index].Gaps[direction] < Near))
            .ThenBy(index => Enumerable.Range(0, 4)
                .Min(direction => pieces[index].Gaps[direction]))
            .First();
        
        var gaps = pieces[anchor].Gaps;
        int[] sides = Enumerable.Range(0, 4)
            .Where(direction => gaps[direction] < Near)
            .ToArray();
        
        if (sides.Length == 0 && example.Spot != PartSpot.Center)
        {
            sides = [Enumerable.Range(0, 4).MinBy(direction => gaps[direction])];
        }

        var spots = sides.Length == 0 ? room.Floor : room.GetWallCells(sides);
        
        var axes = Axes
            .Where(axis => sides.All(side => (side < 2 ? axis.U : axis.V) == 0))
            .ToArray();
        
        string row = example.Kind + string.Concat(sides);
        var previous = axes.Length == 0 ? null : room.Origins.GetValueOrDefault(row);
        var zero = lattice.ToWorld(0, 0);
        var rows = kind.Spacing is not null && previous is not null
            ? previous.SelectMany(from => axes.Select(axis =>
            {
                var step = lattice.ToWorld(axis.U, axis.V);
                var direction = Vector2.Normalize(new Vector2(step.X - zero.X, step.Y - zero.Y));
                return new PointF(from.X + direction.X * kind.Spacing.Value, from.Y + direction.Y * kind.Spacing.Value);
            })).OrderBy(_ => rng.Next()).ToList()
            : [];
        var cells = Sample(rng, spots, SpotTries).Select(cell =>
        {
            float u = cell.U + 0.5f, v = cell.V + 0.5f;
            foreach (int side in sides)
            {
                float offset = gaps[side] / lattice.CellStep;
                if (side < 2)
                {
                    u = side == 0 ? cell.U + offset : cell.U + 1 - offset;
                }
                else
                {
                    v = side == 2 ? cell.V + offset : cell.V + 1 - offset;
                }
            }

            var origin = lattice.ToWorld(u, v);
            return new PointF(origin.X - pieces[anchor].X, origin.Y - pieces[anchor].Y);
        });
        foreach ((float x0, float y0) in rng.NextSingle() < kind.RowShare ? rows.Concat(cells) : cells.Concat(rows))
        {
            var footprints = new HashSet<Cell>();
            var spans = new List<Footprint>();
            bool fits = true;
            foreach (var piece in pieces)
            {
                float x = x0 + piece.X, y = y0 + piece.Y;
                if (!room.Cells.Contains(lattice.GetCell(x, y)) || !MatchesWalls(sides, piece.Gaps, WallGaps.Measure(lattice, room.Cells, layout.Edges, x, y))
                    || !standing.Allows(piece.Id, piece.Facing, piece.Z, x, y))
                {
                    fits = false;
                    break;
                }

                var footprint = Dresser.GetFootprint(lattice, _objects, piece.Id, x, y, 0);
                if (footprint.Overlaps(keepClear))
                {
                    fits = false;
                    break;
                }

                if (piece.Z < FloorHeight && piece.Id.Family is not null)
                {
                    var box = GetFootprint(piece, x, y);
                    if (room.Boxes.Any(other => other.Overlaps(box)))
                    {
                        fits = false;
                        break;
                    }

                    spans.Add(box);
                }

                footprint.IntersectWith(room.Cells);
                footprints.UnionWith(footprint);
            }

            if (!fits || !KeepsFloorJoined(room.Cells, room.Taken, footprints))
            {
                continue;
            }

            room.Taken.UnionWith(footprints);
            room.Boxes.AddRange(spans);
            room.AddOrigin(row, new PointF(x0, y0));
            foreach (var piece in pieces)
            {
                (float pu, float pv) = lattice.ToLattice(x0 + piece.X, y0 + piece.Y);
                placed.Add(new PlacedDecor(piece.Id, pu, pv, piece.Z, piece.Facing, piece.Id == KnownObject.Crate ? KnownObject.CreateEmptyCratePayload() : piece.Payload, (SourceLevel, placed.Count), null));
            }

            return true;
        }

        return false;
    }

    // The floor of a room being furnished, its recipe and the parts drawn for it, what stands on it, and the level's use of examples and
    // pieces so far
    private sealed class Room
    {
        private readonly Dictionary<(string, int), int> _sources = [];
        private readonly Dictionary<string, List<PointF>> _origins = [];
        private readonly IReadOnlyDictionary<EdgeKey, EdgeKind> _edges;

        public Room(HashSet<Cell> cells, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, HashSet<RoomPart> used, Dictionary<KnownObject, int> caps, Dictionary<KnownObject, int> counts)
        {
            Cells = cells;
            Floor = cells.ToArray();
            _edges = edges;
            Used = used;
            Caps = caps;
            Counts = counts;
        }

        public required RoomRecipe Recipe { get; init; }

        public required RecipePart[] Parts { get; init; }

        public required RecipePart[] Pooled { get; init; }

        public required float Units { get; init; }

        public required int Wanted { get; init; }

        public required Queue<(RecipePart Part, RoomPart Example)> Picks { get; init; }

        public int Pieces { get; set; }

        public int Misses { get; set; }

        public bool IsDone => Picks.Count == 0 && (Pieces >= Wanted || Misses >= MaxMisses);

        public HashSet<Cell> Cells { get; }

        public Cell[] Floor { get; }

        public HashSet<Cell> Taken { get; } = [];

        public List<Footprint> Boxes { get; } = [];

        public HashSet<RoomPart> Used { get; }

        public Dictionary<KnownObject, int> Caps { get; }

        public Dictionary<KnownObject, int> Counts { get; }

        public Dictionary<(string, int), int> Sources => _sources;

        public IReadOnlyDictionary<string, List<PointF>> Origins => _origins;

        public bool IsWithinCaps(RoomPart example) =>
            example.Pieces.GroupBy(piece => piece.Id).All(group => Counts.GetValueOrDefault(group.Key) + group.Count() <= Caps.GetValueOrDefault(group.Key));

        public void Count(RoomPart example)
        {
            foreach (var piece in example.Pieces)
            {
                Counts[piece.Id] = Counts.GetValueOrDefault(piece.Id) + 1;
            }
        }

        public void AddOrigin(string kind, PointF origin)
        {
            if (!_origins.TryGetValue(kind, out var list))
            {
                _origins[kind] = list = [];
            }

            list.Add(origin);
        }

        // Return the cells with a wall or the room's edge on every given side, the sides numbered as in WallGaps
        public Cell[] GetWallCells(int[] sides) => Floor.Where(cell => sides.All(side =>
        {
            var (neighbor, edge) = EdgeKey.GetNeighbors(cell)[side switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 }];
            return !Cells.Contains(neighbor) || _edges.ContainsKey(edge);
        })).ToArray();
    }

    // The floor a piece takes along the lattice axes, in map pixels
    private readonly record struct Footprint(float U, float V, float HalfU, float HalfV)
    {
        public bool Overlaps(Footprint other) => MathF.Abs(U - other.U) < HalfU + other.HalfU - 1 && MathF.Abs(V - other.V) < HalfV + other.HalfV - 1;
    }

    // Turn and stand a piece only as the originals do by the walls around it
    private readonly record struct Standing(Lattice Lattice, IReadOnlyDictionary<EdgeKey, EdgeKind> Walls, FacingTable Facings, WallClearanceTable Clearance)
    {
        public bool Allows(KnownObject id, byte facing, float z, float x, float y)
        {
            (float u, float v) = Lattice.ToLattice(x, y);
            return Facings.Fits(id, facing, Walls, u, v)
                   && !(id.IsWallMounted && !Facings.Hangs(id, facing, z, Walls, u, v))
                   && !(z < FloorHeight && Clearance.TryGetClearance(id, out float minimum, out _) && WallClearanceTable.MeasureDistance(Walls, u, v) < minimum - ClearanceSlack);
        }
    }

    // Keep a piece as close as in its source to each wall the part stands against, and no closer than in its source to any other wall
    private static bool MatchesWalls(int[] sides, WallGaps source, WallGaps target)
    {
        for (int direction = 0; direction < 4; direction++)
        {
            bool against = sides.Contains(direction) && source[direction] < WallGaps.Far;
            if (against ? Math.Abs(source[direction] - target[direction]) > Tolerance : target[direction] < Math.Min(source[direction], Near) - Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool KeepsFloorJoined(HashSet<Cell> cells, HashSet<Cell> taken, HashSet<Cell> footprints)
    {
        var free = cells.Where(cell => !taken.Contains(cell) && !footprints.Contains(cell)).ToHashSet();
        if (free.Count == 0)
        {
            return false;
        }

        var first = free.First();
        var reached = new HashSet<Cell> { first };
        var queue = new Queue<Cell>([first]);
        while (queue.Count > 0)
        {
            foreach (var (neighbor, _) in EdgeKey.GetNeighbors(queue.Dequeue()))
            {
                if (free.Contains(neighbor) && reached.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return reached.Count == free.Count;
    }

    // Lay papers on free floor at the recipe's rate, each turned and stood as the originals allow there
    private void Scatter(Random rng, Lattice layoutLattice, HashSet<Cell> cells, HashSet<Cell> taken, RoomRecipe recipe, float units, Standing standing, List<PlacedDecor> placed)
    {
        if (!recipe.ScatterRates.TryGetValue(KnownObject.ScatteredPaper, out float rate))
        {
            return;
        }

        var free = cells.Where(cell => !taken.Contains(cell)).ToArray();
        int count = Math.Min(free.Length, (int)MathF.Round(rate * units * (DensityJitter.Min + (DensityJitter.Max - DensityJitter.Min) * rng.NextSingle())));
        float z = _heightOf(KnownObject.ScatteredPaper);
        foreach (var cell in free.OrderBy(_ => rng.Next()).Take(count))
        {
            (float u, float v, byte facing) = (cell.U + rng.NextSingle(), cell.V + rng.NextSingle(), (byte)rng.Next(256));
            var point = layoutLattice.ToWorld(u, v);
            if (standing.Allows(KnownObject.ScatteredPaper, facing, z, point.X, point.Y))
            {
                placed.Add(new PlacedDecor(KnownObject.ScatteredPaper, u, v, z, facing, [], (SourceLevel, placed.Count), null));
            }
        }
    }
}

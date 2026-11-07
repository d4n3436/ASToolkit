using System.Buffers;
using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
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
    private const int MinPieces = 3;
    private static readonly (int U, int V)[] Axes = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private const int SourceLevel = -2;
    private const float FloorHeight = 5;
    private static readonly (float Min, float Max) DensityJitter = (0.8f, 1.2f);

    private readonly Dictionary<RoomType, (RoomRecipe Recipe, RecipePart[] Parts)> _recipes;
    private readonly ObjectTable _objects;
    private readonly Func<KnownObject, float, bool> _allows;
    private readonly Lattice _lattice;
    private readonly Dictionary<KnownObject, float> _spans;

    /// <summary>
    /// Initializes a furnisher with the recipes of a floor set, keeping only the parts whose pieces the level may hold.
    /// </summary>
    /// <param name="recipes">The recipes of the floor set's room types.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="allows">Returns whether the level may hold an object at a height.</param>
    /// <param name="lattice">A lattice with the axes of the rooms' walls.</param>
    public RoomFurnisher(IEnumerable<RoomRecipe> recipes, ObjectTable objects, Func<KnownObject, float, bool> allows, Lattice lattice)
    {
        _objects = objects;
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
    /// <param name="occupancy">The objects already standing and the doorways to keep clear.</param>
    /// <param name="companionsOf">Returns the objects that lie by an anchor object in the level.</param>
    /// <param name="wallLattice">The lattice the built walls are read on.</param>
    /// <param name="walls">The edges of the walls as built.</param>
    /// <param name="facings">How the originals turn pieces against walls.</param>
    /// <param name="clearance">How close the originals stand pieces to walls.</param>
    public (Dictionary<int, RoomType> Types, List<PlacedDecor> Decor) Furnish(Random rng, LayoutPlan plan, FineLayout layout, RoomAtlas original,
        IReadOnlySet<Cell> keepClear, LevelOccupancy occupancy, Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf, Lattice wallLattice, IReadOnlyDictionary<EdgeKey, EdgeKind> walls, FacingTable facings,
        WallClearanceTable clearance)
    {
        var own = Prepare(RoomRecipe.Measure(original.Rooms, layout.Lattice));
        var types = GetTypes(plan);
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

            var (_, parts) = own.TryGetValue(type, out var level) && level.Parts.Length > 0 ? level : pooled;
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
                Parts = parts,
                Pooled = pooled.Parts,
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
                    room.Pieces += TryPlace(rng, layout, keepClear, occupancy, companionsOf, room, pick.Part, standing, placed, pick.Example);
                    continue;
                }

                bool fromLevel = rng.NextSingle() < OwnShare;
                int count = TryPlace(rng, layout, keepClear, occupancy, companionsOf, room, Draw(rng, fromLevel ? room.Parts : room.Pooled), standing, placed);
                if (count == 0 && fromLevel)
                {
                    count = TryPlace(rng, layout, keepClear, occupancy, companionsOf, room, Draw(rng, room.Pooled), standing, placed);
                }

                room.Pieces += count;
                room.Misses = count > 0 ? 0 : room.Misses + 1;
            }
        }

        return (types, placed);
    }

    private static bool IsFurnished(AtlasRoom room) => room.IsRoom || room.Role == RoomRole.Secret;

    private static Dictionary<int, RoomType> GetTypes(LayoutPlan plan) => plan.Regions
        .Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.SecretRoom)
        .ToDictionary(region => region.Id, region => region.IsHall ? RoomType.Hall : region.Type ?? RoomType.Passage);

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
    private int TryPlace(Random rng, FineLayout layout, IReadOnlySet<Cell> keepClear, LevelOccupancy occupancy, Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf, Room room, RecipePart part, Standing standing, List<PlacedDecor> placed,
        RoomPart? first = null)
    {
        for (int attempt = 0; attempt < ExampleTries; attempt++)
        {
            var example = attempt == 0 && first is not null ? first : Pick(rng, part);
            bool tracked = example.Pieces.Count >= MinTrackedPieces;
            if (tracked && (room.Used.Contains(example) || room.Sources.GetValueOrDefault(example.Room) >= MaxPartsPerSource) || !room.IsWithinCaps(example)
                || !TryPlace(rng, layout, keepClear, occupancy, companionsOf, room, example, part, standing, placed))
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
    private bool TryPlace(Random rng, FineLayout layout, IReadOnlySet<Cell> keepClear, LevelOccupancy occupancy, Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf, Room room, RoomPart example, RecipePart kind, Standing standing,
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
            })).OrderBy(_ => rng.Next()).Take(SpotTries).ToList()
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
        var companions = DrawCompanions(rng, pieces, companionsOf);
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

                if (piece is { Z: < FloorHeight, Id.Family: not null })
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

            var inRoom = companions.Where(companion => room.Cells.Contains(lattice.GetCell(x0 + companion.X, y0 + companion.Y))).ToArray();
            if (!fits || companions.Except(inRoom).Any(companion => _objects.GetBlocking(companion.Id) != BlockingClass.WalkThrough))
            {
                continue;
            }

            foreach (var companion in inRoom)
            {
                var footprint = Dresser.GetFootprint(lattice, _objects, companion.Id, x0 + companion.X, y0 + companion.Y, 0);
                footprint.IntersectWith(room.Cells);
                footprints.UnionWith(footprint);
            }

            var group = pieces.Select(piece => (piece.Id, X: x0 + piece.X, Y: y0 + piece.Y, piece.Z))
                .Concat(inRoom.Select(companion => (companion.Id, X: x0 + companion.X, Y: y0 + companion.Y, companion.Z)))
                .ToArray();
            if (!room.KeepsFloorJoined(footprints) || !occupancy.FitsAll(group))
            {
                continue;
            }

            room.Take(footprints);
            room.Boxes.AddRange(spans);
            foreach ((var id, float x, float y, float z) in group)
            {
                occupancy.Add(id, x, y, z);
            }

            room.AddOrigin(row, new PointF(x0, y0));
            foreach (var piece in pieces)
            {
                (float pu, float pv) = lattice.ToLattice(x0 + piece.X, y0 + piece.Y);
                placed.Add(new PlacedDecor(piece.Id, pu, pv, piece.Z, piece.Facing, piece.Id == KnownObject.Crate ? KnownObject.CreateEmptyCratePayload() : piece.Payload, (SourceLevel, placed.Count), null, SpriteSource.CopiedPart));
            }

            foreach (var companion in inRoom)
            {
                (float pu, float pv) = lattice.ToLattice(x0 + companion.X, y0 + companion.Y);
                placed.Add(new PlacedDecor(companion.Id, pu, pv, companion.Z, companion.Facing, [], (SourceLevel, placed.Count), null, SpriteSource.Relation));
            }

            return true;
        }

        return false;
    }

    // Draw the companions of every piece at their anchors' rates
    private static List<PartPiece> DrawCompanions(Random rng, IReadOnlyList<PartPiece> pieces, Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf)
    {
        var drawn = new List<PartPiece>();
        foreach (var piece in pieces)
        {
            foreach (var companion in companionsOf(piece.Id))
            {
                foreach (var sample in companion.Draw(rng, piece.Facing))
                {
                    drawn.Add(new PartPiece(companion.Id, piece.X + sample.Dx, piece.Y + sample.Dy, piece.Z + sample.Dz, sample.Facing, [], default));
                }
            }
        }

        return drawn;
    }

    // The floor of a room being furnished, its recipe and the parts drawn for it, what stands on it, and the level's use of examples and
    // pieces so far
    private sealed class Room
    {
        private readonly Dictionary<string, List<PointF>> _origins = [];
        private readonly IReadOnlyDictionary<EdgeKey, EdgeKind> _edges;
        private readonly Dictionary<int, Cell[]> _wallCells = [];
        private readonly int _u0;
        private readonly int _v0;
        private readonly int _width;
        private readonly int _height;
        private readonly bool[] _cells;
        private readonly bool[] _taken;

        public Room(HashSet<Cell> cells, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, HashSet<RoomPart> used, Dictionary<KnownObject, int> caps, Dictionary<KnownObject, int> counts)
        {
            Cells = cells;
            _u0 = cells.Min(cell => cell.U);
            _v0 = cells.Min(cell => cell.V);
            _width = cells.Max(cell => cell.U) - _u0 + 1;
            _height = cells.Max(cell => cell.V) - _v0 + 1;
            _cells = new bool[_width * _height];
            _taken = new bool[_width * _height];
            foreach (var cell in cells)
            {
                _cells[GetIndex(cell)] = true;
            }

            Floor = cells.ToArray();
            _edges = edges;
            Used = used;
            Caps = caps;
            Counts = counts;
        }

        public required RecipePart[] Parts { get; init; }

        public required RecipePart[] Pooled { get; init; }

        public required int Wanted { get; init; }

        public required Queue<(RecipePart Part, RoomPart Example)> Picks { get; init; }

        public int Pieces { get; set; }

        public int Misses { get; set; }

        public bool IsDone => Picks.Count == 0 && (Pieces >= Wanted || Misses >= MaxMisses);

        public HashSet<Cell> Cells { get; }

        public Cell[] Floor { get; }

        public bool KeepsFloorJoined(HashSet<Cell> footprints)
        {
            int size = _width * _height;
            bool[] blocked = ArrayPool<bool>.Shared.Rent(size);
            int[] stack = ArrayPool<int>.Shared.Rent(size);
            try
            {
                for (int index = 0; index < size; index++)
                {
                    blocked[index] = !_cells[index] || _taken[index];
                }

                foreach (var cell in footprints)
                {
                    blocked[GetIndex(cell)] = true;
                }

                int free = 0, first = -1;
                for (int index = 0; index < size; index++)
                {
                    if (!blocked[index])
                    {
                        free++;
                        first = first < 0 ? index : first;
                    }
                }

                if (free == 0)
                {
                    return false;
                }

                int top = 0, reached = 1;
                stack[top++] = first;
                blocked[first] = true;
                while (top > 0)
                {
                    int at = stack[--top];
                    int u = at % _width, v = at / _width;
                    ReadOnlySpan<int> neighbors = [u + 1 < _width ? at + 1 : -1, u > 0 ? at - 1 : -1, v + 1 < _height ? at + _width : -1, v > 0 ? at - _width : -1];
                    foreach (int next in neighbors)
                    {
                        if (next >= 0 && !blocked[next])
                        {
                            blocked[next] = true;
                            stack[top++] = next;
                            reached++;
                        }
                    }
                }

                return reached == free;
            }
            finally
            {
                ArrayPool<bool>.Shared.Return(blocked);
                ArrayPool<int>.Shared.Return(stack);
            }
        }

        public void Take(HashSet<Cell> footprints)
        {
            foreach (var cell in footprints)
            {
                _taken[GetIndex(cell)] = true;
            }
        }

        private int GetIndex(Cell cell) => (cell.V - _v0) * _width + cell.U - _u0;

        public List<Footprint> Boxes { get; } = [];

        public HashSet<RoomPart> Used { get; }

        public Dictionary<KnownObject, int> Caps { get; }

        public Dictionary<KnownObject, int> Counts { get; }

        public Dictionary<(string, int), int> Sources { get; } = [];

        public IReadOnlyDictionary<string, List<PointF>> Origins => _origins;

        public bool IsWithinCaps(RoomPart example) =>
            example.PieceCounts.All(pair => Counts.GetValueOrDefault(pair.Key) + pair.Value <= Caps.GetValueOrDefault(pair.Key));

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
        public Cell[] GetWallCells(int[] sides)
        {
            int key = sides.Aggregate(0, (mask, side) => mask | 1 << side);
            if (!_wallCells.TryGetValue(key, out var cells))
            {
                _wallCells[key] = cells = Floor.Where(cell =>
                {
                    var walled = _edges.GetWalledSides(cell, Cells.Contains, static _ => true);
                    return sides.All(side => walled.HasFlag(WallGaps.Sides[side]));
                }).ToArray();
            }

            return cells;
        }
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
            var wall = Walls.FindNearestWall(u, v);
            return Facings.Fits(id, facing, wall)
                   && !(id.IsWallMounted && !Facings.Hangs(id, facing, z, wall, Walls, u, v))
                   && !(z < FloorHeight && Clearance.TryGetClearance(id, out float minimum, out _) && Walls.MeasureClearance(u, v) < minimum - EdgeMapExtensions.WallSlack);
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
}

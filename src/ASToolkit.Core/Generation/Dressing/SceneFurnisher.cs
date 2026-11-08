using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using static ASToolkit.Core.Generation.Dressing.FurnitureGrammar;
using static ASToolkit.Core.Generation.Dressing.RoomGeometry;

namespace ASToolkit.Core.Generation.Dressing;

internal sealed class SceneFurnisher
{
    private const int BigCells = 200;
    private const int BareCells = 6;
    private const float MinStep = 0.5f;
    private const int Tries = 12;
    private const int GrowTries = 40;
    private const float HeapReach = 3;
    private const int SourceLevel = -3;

    private readonly FurnitureGrammar _grammar;
    private readonly ObjectTable _objects;
    private readonly bool _campaign;
    private readonly Random _rng;
    private readonly FineLayout _layout;
    private readonly LevelOccupancy _occupancy;
    private readonly IReadOnlySet<Cell> _reserved;
    private readonly WallClearanceTable _clearance;
    private readonly float _sizeShare;
    private readonly List<PlacedDecor> _placed = [];

    private RoomGeometry _room = null!;
    private RoomProgram _program = null!;
    private HashSet<Cell> _blocked = [];
    private List<(PointF Point, KnownObject Id, byte Dir, RectangleF Box)> _floor = [];
    private Dictionary<PieceFamily, (KnownObject? Main, KnownObject? Second, float Share)> _palette = [];
    private Dictionary<Arrangement, int> _placedCounts = [];
    private int _anchors;
    private bool _force;

    public SceneFurnisher(FurnitureGrammar grammar, ObjectTable objects, GenerationStyle style, Random rng, FineLayout layout, LevelOccupancy occupancy,
        IReadOnlySet<Cell> reserved, WallClearanceTable clearance)
    {
        _grammar = grammar;
        _objects = objects;
        _campaign = style == GenerationStyle.Campaign;
        _rng = rng;
        _layout = layout;
        _occupancy = occupancy;
        _reserved = reserved;
        _clearance = clearance;

        // Read unmeasured footprints off the object's size, scaled by the median share the measured families take
        _sizeShare = grammar.Families.Values
            .Where(family => family.Extent > 0)
            .Select(family => family.Extent / GetSizeSteps(family.Pieces[0]))
            .Order()
            .ElementAt(grammar.Families.Values.Count(family => family.Extent > 0) / 2);
    }

    public List<PlacedDecor> Furnish(LayoutPlan plan)
    {
        var skipped = plan.Gate == GateKind.Fences ? plan.GeneratorRooms.Select(entry => entry.Room.Id).Append(plan.KeyRoom?.Id ?? -1).ToHashSet() : [];
        foreach (var region in plan.Regions.Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.SecretRoom && !skipped.Contains(region.Id)))
        {
            var type = region.IsHall ? RoomType.Hall : region.Type ?? RoomType.Passage;
            type = _grammar.Aliases.GetValueOrDefault(type, type);
            if (_grammar.Types.TryGetValue(type, out var program))
            {
                FurnishRoom(new RoomGeometry(_layout, _layout.GetRegionCells(region.Id), _reserved), program);
            }
        }

        return _placed;
    }

    private void FurnishRoom(RoomGeometry room, RoomProgram program)
    {
        _room = room;
        _program = program;
        _blocked = [];
        _floor = [];
        _palette = [];
        _placedCounts = [];
        _anchors = 0;

        float target = Draw(program.PiecesPerHundredCells[GetBand(DensityBands)]) * room.HalfCells / 100;
        bool big = room.HalfCells >= BigCells;
        var fitting = program.Arrangements.Where(IsRoomLargeEnough).ToArray();
        var lead = fitting.FirstOrDefault(arrangement => arrangement.Signature);
        var held = fitting.Where(arrangement => _rng.NextSingle() < (arrangement == lead ? program.SignatureShare : big ? arrangement.BigShare : arrangement.Share)).ToArray();

        // Stand the signature first with its full module
        for (int attempt = 0; lead is not null && held.Contains(lead) && attempt < Tries; attempt++)
        {
            if (!Place(lead, true))
                continue;

            _placedCounts[lead] = 1;
            break;
        }

        foreach (var arrangement in held)
        {
            // Round the count at random to keep its expectation in rooms too small for one
            int count = (int)MathF.Floor(Draw(arrangement.PerHundredCells) * room.HalfCells / 100 + _rng.NextSingle()) - (arrangement == lead ? 1 : 0);
            for (int index = 0; index < count && _anchors < target; index++)
            {
                _placedCounts[arrangement] = _placedCounts.GetValueOrDefault(arrangement) + (Place(arrangement, false) ? 1 : 0);
            }
        }

        var growing = held.Where(arrangement => arrangement.Kind is not (ArrangementKind.Open or ArrangementKind.Near)).ToArray();
        float[] rates = growing.Select(arrangement => arrangement.Share * arrangement.PerHundredCells.Sum()).ToArray();
        int[] misses = new int[growing.Length];
        while (_anchors < target && rates.Any(rate => rate > 0))
        {
            int index = DrawIndex(rates);
            var arrangement = growing[index];
            bool landed = Place(arrangement, false);
            _placedCounts[arrangement] = _placedCounts.GetValueOrDefault(arrangement) + (landed ? 1 : 0);
            misses[index] = landed ? 0 : misses[index] + 1;
            bool full = _placedCounts[arrangement] >= MathF.Ceiling(arrangement.MaxPerHundredCells * room.HalfCells / 100);
            rates[index] = misses[index] < GrowTries && !full ? rates[index] : 0;
        }

        var cover = lead?.Kind is ArrangementKind.Far or ArrangementKind.Heap
            ? lead
            : program.Arrangements.FirstOrDefault(arrangement => arrangement.Kind == ArrangementKind.Far);

        if (program.CoverFarWalls && cover is not null)
        {
            CoverFarWalls(cover);
        }
    }

    private void CoverFarWalls(Arrangement cover)
    {
        foreach ((int face, var cells) in _room.Walls.Where(wall => (wall.Face & FarFaces) != 0).ToArray())
        {
            for (int index = 0, bare = 0; index < cells.Length; index++)
            {
                var center = new PointF(cells[index].U + 0.5f, cells[index].V + 0.5f);
                bare = _floor.Any(piece => MathF.Abs(piece.Point.X - center.X) <= 1.5f && MathF.Abs(piece.Point.Y - center.Y) <= 1.5f) ? 0 : bare + 1;
                if (bare != BareCells)
                    continue;

                var stretch = cells[(index + 1 - BareCells)..(index + 1)];
                if (cover.Kind == ArrangementKind.Heap)
                {
                    PlaceHeap(0, 0, (face, stretch[BareCells / 2]));
                }
                else
                {
                    PlaceRun(face, stretch, Along(stretch[0], face) + 0.5f, cover.Family!.Value, cover.Members, BareCells);
                }

                bare = 0;
            }
        }
    }

    private bool IsRoomLargeEnough(Arrangement arrangement)
    {
        var family = arrangement.Family is null ? null : _grammar.Families.GetValueOrDefault(arrangement.Family.Value);
        return family is null || _room.HalfCells >= (_campaign ? family.CampaignMinCells : family.MinCells);
    }

    private bool Place(Arrangement arrangement, bool force)
    {
        int before = _anchors;
        int modules = _rng.Next(arrangement.Modules[0], arrangement.Modules[1] + 1);
        var id = Pick(arrangement.Family);

        // Turn open-floor pieces to the walls at their family's wall share
        bool open = _room.Cells.Count >= arrangement.OpenFromCells && id is not null && _rng.NextSingle() >= GetWallShare(id.Value);
        _force = force;
        switch (arrangement.Kind)
        {
            case ArrangementKind.Grid or ArrangementKind.Open when open && _room.Interior.Length > 0:
                PlaceGrid(arrangement.Family!.Value, arrangement.Kind == ArrangementKind.Open ? 1 : modules);
                break;
            case ArrangementKind.Far or ArrangementKind.Near or ArrangementKind.Grid or ArrangementKind.Open:
                bool far = arrangement.Kind != ArrangementKind.Near;
                var runs = _room.Walls.Where(wall => (wall.Face & FarFaces) != 0 == far).ToArray();
                if (runs.Length > 0)
                {
                    (int face, var cells) = runs[DrawIndex(runs.Select(run => (float)run.Cells.Length).ToArray())];
                    PlaceRun(face, cells, Along(cells[0], face) + 0.5f + _rng.NextSingle() * (cells.Length - 1), arrangement.Family!.Value, arrangement.Members,
                        arrangement.Kind == ArrangementKind.Open ? 1 : modules);
                }

                break;
            case ArrangementKind.Heap:
                PlaceHeap(arrangement.OpenShare, arrangement.NearShare, null);
                break;
        }

        _force = false;
        return _anchors > before;
    }

    private void PlaceRun(int face, Cell[] cells, float along, PieceFamily lead, Dictionary<PieceFamily, float> members, int modules)
    {
        for (int placed = 0; placed < modules && along < Along(cells[^1], face) + 1;)
        {
            var family = placed == 0 || members.Count == 0 ? lead : Draw(members);
            var id = Pick(family);
            if (id is null)
            {
                break;
            }

            var wall = cells[Math.Min(cells.Length - 1, (int)(along - Along(cells[0], face)))];
            var byFace = _grammar.Facings.GetValueOrDefault(id.Value);
            var facings = byFace?.GetValueOrDefault(face) ?? byFace?.GetValueOrDefault(Open) ?? [];
            placed += PlaceModule(id.Value, FromWall(wall, face, _grammar.Families[family].WallDistance, along), DrawFacing(id.Value, facings), face) ? 1 : 0;
            along += MathF.Max(MinStep, _grammar.Families[family].Step);
        }
    }

    private void PlaceGrid(PieceFamily family, int modules)
    {
        var id = Pick(family);
        if (id is null)
        {
            return;
        }

        var rule = _grammar.Families[family];
        var cells = _room.Cells;
        float step = MathF.Max(GetExtent(id.Value), rule.GridStep), across = MathF.Max(GetExtent(id.Value), rule.GridAcross);
        bool alongU = cells.Max(cell => cell.U) - cells.Min(cell => cell.U) >= cells.Max(cell => cell.V) - cells.Min(cell => cell.V);
        int rows = modules >= 4 ? 2 : 1;
        for (int attempt = 0, placed = 0; attempt < Tries && placed == 0; attempt++)
        {
            var seed = _room.Interior[_rng.Next(_room.Interior.Length)];
            for (int index = 0; index < modules; index++)
            {
                int row = index / rows;
                float a = row * step, b = index % rows * across;
                var point = alongU ? new PointF(seed.U + 0.5f + a, seed.V + 0.5f + b) : new PointF(seed.U + 0.5f + b, seed.V + 0.5f + a);
                placed += _room.Interior.Contains(_room.GetCell(point)) && PlaceModule(id.Value, point, GetFacing(id.Value, point)) ? 1 : 0;
            }
        }
    }

    private void PlaceHeap(float openShare, float nearShare, (int Face, Cell Wall)? at)
    {
        var heap = _grammar.Heap;
        var coreFamily = Draw(heap.Core);
        var core = Pick(coreFamily);
        var members = new List<(PointF Point, KnownObject Id)>();
        for (int attempt = 0; core is not null && members.Count == 0 && attempt < Tries; attempt++)
        {
            var point = GetHeapSpot(core.Value, openShare, nearShare, at);
            if (point is not null && PlaceModule(core.Value, point.Value, GetFacing(core.Value, point.Value)))
            {
                members.Add((point.Value, core.Value));
            }
        }

        for (int index = 1, size = _rng.Next(heap.Size[0], heap.Size[1] + 1); members.Count > 0 && index < size; index++)
        {
            var id = Pick(Draw(heap.Mix.GetValueOrDefault(coreFamily, heap.Core)));
            if (id is null)
            {
                continue;
            }

            var piece = id.Value;
            bool byWall = _rng.NextSingle() < GetWallShare(piece);

            var origin = members[0].Point;
            float distance = GetRule(piece)?.WallDistance ?? MinStep;

            // Fall back on the nearest free wall spots when none beside the heap stands at a wall
            var along = !byWall ? [] : _room.Walls
                .SelectMany(wall => wall.Cells.Select(cell => FromWall(cell, wall.Face, distance, Along(cell, wall.Face) + 0.5f)))
                .Where(spot => MathF.Abs(spot.X - origin.X) + MathF.Abs(spot.Y - origin.Y) <= HeapReach)
                .OrderBy(spot => MathF.Abs(spot.X - origin.X) + MathF.Abs(spot.Y - origin.Y))
                .ToArray();

            // Order members at random, weighted by how often their families neighbor the piece's
            var landed = members
                .OrderByDescending(member => MathF.Pow(_rng.NextSingle(), 1 / MathF.Max(1, GetPairValue(heap.Pairs, member.Id, piece, 0))))
                .SelectMany(member => GetHeapOffsets(member.Id, piece).OrderBy(_ => _rng.Next()).Select(offset => new PointF(member.Point.X + offset.X, member.Point.Y + offset.Y)))
                .Where(spot => _room.WallSteps.GetValueOrDefault(_room.GetCell(spot)) == 0 == byWall)
                .Concat(along)
                .Cast<PointF?>()
                .FirstOrDefault(spot => PlaceModule(piece, spot!.Value, GetFacing(piece, spot.Value)));

            if (landed is not null)
            {
                members.Add((landed.Value, piece));
            }
        }
    }

    private PointF? GetHeapSpot(KnownObject id, float openShare, float nearShare, (int Face, Cell Wall)? at)
    {
        float distance = GetRule(id)?.WallDistance ?? 0;
        if (at is null && _room.Walls.Count == 0)
        {
            return null;
        }

        if (at is not null)
        {
            return FromWall(at.Value.Wall, at.Value.Face, distance, Along(at.Value.Wall, at.Value.Face) + 0.5f);
        }

        if (_rng.NextSingle() < MathF.Min(openShare, 1 - GetWallShare(id)) && _room.Interior.Length > 0)
        {
            var cell = _room.Interior[_rng.Next(_room.Interior.Length)];
            return new PointF(cell.U + 0.5f, cell.V + 0.5f);
        }

        bool near = _rng.NextSingle() < nearShare;
        var sides = _room.Walls.Where(wall => (wall.Face & FarFaces) == 0 == near).DefaultIfEmpty(_room.Walls[0]).ToArray();
        (int face, var cells) = sides[_rng.Next(sides.Length)];
        var wall = cells[_rng.Next(cells.Length)];
        return FromWall(wall, face, distance, Along(wall, face) + 0.5f);
    }

    private bool PlaceModule(KnownObject id, PointF point, byte dir, int lining = 0)
    {
        // Keep anchors off each other's footprints, a wall piece taking only its depth across the wall
        float extent = GetExtent(id), depth = lining == 0 ? extent : MathF.Min(extent, 2 * (GetRule(id)?.WallDistance ?? MinStep));
        float spanU = lining is PlusU or MinusU ? depth : extent, spanV = lining is PlusU or MinusU ? extent : depth;
        var box = new RectangleF(point.X - spanU / 2, point.Y - spanV / 2, spanU, spanV);
        if (!IsInside(id, point, dir, lining) || _floor.Any(piece => RectangleF.Intersect(piece.Box, box) is { Width: > 0.02f, Height: > 0.02f }) || !Commit(id, point, 0, dir, lining))
        {
            return false;
        }

        _floor[^1] = (point, id, dir, box);
        _anchors++;

        var slots = GetModules(id).GetValueOrDefault(dir, []).GroupBy(companion => (companion.Id.Family, companion.Slot)).ToArray();
        var stood = new HashSet<(PieceFamily? Family, int Slot)>();
        foreach (var slot in slots)
        {
            // Stand a stacked piece only on the tier below it
            var options = slot.ToArray();
            var below = slots.FirstOrDefault(other => other.Key == (slot.Key.Family, slot.Key.Slot - 1));
            if (below is not null && below.Min(companion => companion.Z) < options.Min(companion => companion.Z) && !stood.Contains(below.Key))
            {
                continue;
            }

            if ((!_force || slot.Key.Slot != 0) && !(_rng.NextSingle() < options.Sum(option => option.Share)))
                continue;

            var companion = options[DrawIndex(options.Select(option => option.Share).ToArray())];
            if (Commit(companion.Id, new PointF(point.X + companion.U, point.Y + companion.V), companion.Z, companion.Facing))
            {
                stood.Add(slot.Key);
            }
        }

        _force = false;
        foreach (var attached in _grammar.Attached.GetValueOrDefault(id) ?? [])
        {
            if (_rng.NextSingle() < attached.Share)
            {
                Commit(attached.Id, new PointF(point.X + attached.U, point.Y + attached.V), 0, dir);
            }
        }

        return true;
    }

    private bool Commit(KnownObject id, PointF point, float z, byte dir, int lining = -1)
    {
        var world = _layout.Lattice.ToWorld(point.X, point.Y);
        var cell = _room.GetCell(point);
        var footprint = z > 0 ? [] : Dresser.GetFootprint(_layout.Lattice, _objects, id, world.X, world.Y, 0);
        footprint.IntersectWith(_room.Cells);
        if (_campaign && _grammar.AddOnOnly.Contains(id) || !_room.Cells.Contains(cell) || z <= 0 && lining < 0 && !id.IsAttached && !IsInside(id, point, dir, 0) || _reserved.Contains(cell)
            || footprint.Overlaps(_room.Openings) || z <= 0 && !id.IsAttached && _floor.Any(other => IsTooClose(other, id, point, dir, lining >= 0)) || !IsJoinedWithout(footprint)
            || !_occupancy.Fits(id, world.X, world.Y, z))
        {
            return false;
        }

        _blocked.UnionWith(footprint);
        if (z <= 0 && !id.IsAttached)
        {
            _floor.Add((point, id, dir, RectangleF.Empty));
        }

        _occupancy.Add(id, world.X, world.Y, z);
        _placed.Add(new PlacedDecor(id, point.X, point.Y, z, dir, id == KnownObject.Crate ? KnownObject.CreateEmptyCratePayload() : [], (SourceLevel, _placed.Count), null,
            SpriteSource.Model));
        return true;
    }

    private bool IsJoinedWithout(HashSet<Cell> footprint)
    {
        var kept = _room.Kept;
        if (footprint.Count == 0 || kept.Count == 0)
        {
            return true;
        }

        var reached = new HashSet<Cell> { kept.First() };
        var queue = new Queue<Cell>(reached);
        while (queue.TryDequeue(out var cell))
        {
            foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
            {
                if (_room.Cells.Contains(next) && !_blocked.Contains(next) && !footprint.Contains(next) && !(_layout.Edges.TryGetValue(edge, out var kind) && kind.BlocksWalking)
                    && reached.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return kept.All(reached.Contains);
    }

    private bool IsTooClose((PointF Point, KnownObject Id, byte Dir, RectangleF Box) other, KnownObject id, PointF point, byte dir, bool anchor)
    {
        var pair = FamilyPair.FromPieces(other.Id, id);
        float closest = (GetExtent(other.Id) + GetExtent(id)) / 2;
        if (pair is not null && _grammar.Spacing.TryGetValue(pair.Value, out var spacing))
        {
            closest = other.Dir == dir ? spacing.Same : spacing.Turned;
        }

        // Keep pieces apart by their frames' measured spacing, companions only from pieces of their own object
        var otherFrames = _grammar.FrameSpacing.GetValueOrDefault(other.Id);
        var frames = _grammar.FrameSpacing.GetValueOrDefault(id);
        bool anchors = anchor && !other.Box.IsEmpty;
        if ((other.Id == id || anchors) && otherFrames is not null && frames is not null && otherFrames.TryGetValue(other.Dir, out float first) && frames.TryGetValue(dir, out float second))
        {
            closest = MathF.Max(closest, (first + second) / 2);
        }

        float du = other.Point.X - point.X, dv = other.Point.Y - point.Y;
        return du * du + dv * dv < closest * closest;
    }

    // A piece lining a wall also keeps its frame's end clearance from the walls at its ends
    private bool IsInside(KnownObject id, PointF point, byte dir, int lining)
    {
        var rule = GetRule(id);
        float end = _grammar.EndClearance.GetValueOrDefault(id)?.GetValueOrDefault(dir) ?? 0;
        foreach ((int du, int dv, int face) in new[] { (1, 0, PlusU), (0, -1, MinusV), (-1, 0, MinusU), (0, 1, PlusV) })
        {
            float reach = rule is null ? MinStep : (face & FarFaces) != 0 ? rule.FarWallMin : rule.NearWallMin;
            reach = lining != 0 && Along(new Cell(du, dv), lining) != 0 ? MathF.Max(reach, MathF.Max(GetExtent(id) / 2, end)) : reach;
            if (!_room.Cells.Contains(_room.GetCell(new PointF(point.X + du * (reach - 0.02f), point.Y + dv * (reach - 0.02f)))))
            {
                return false;
            }
        }

        return _room.Cells.Contains(_room.GetCell(point)) && (!_clearance.TryGetClearance(id, out float least, out _) || _layout.Edges.MeasureClearance(point.X, point.Y) >= least - 0.01f);
    }

    private FurnitureFamily? GetRule(KnownObject id)
    {
        var family = id.Family;
        return family is null ? null : _grammar.Families.GetValueOrDefault(family.Value);
    }

    private float GetWallShare(KnownObject id)
    {
        var rule = GetRule(id);
        return rule is null ? 0 : rule.WallShare[GetBand(WallBands)];
    }

    private float GetExtent(KnownObject id)
    {
        float extent = GetRule(id)?.Extent ?? 0;
        return extent > 0 ? extent : MathF.Max(MinStep, _sizeShare * GetSizeSteps(id));
    }

    private float GetSizeSteps(KnownObject id) => _objects.TryFind(id, out var obj) ? MathF.Max(obj.Size.X / _layout.Lattice.StepX, obj.Size.Y / _layout.Lattice.StepY) : 1;

    private int GetBand(int[] bands) => Array.FindLastIndex(bands, band => band <= _room.HalfCells);

    private static float GetPairValue(Dictionary<FamilyPair, float> table, KnownObject first, KnownObject second, float fallback)
    {
        var pair = FamilyPair.FromPieces(first, second);
        return pair is null ? fallback : table.GetValueOrDefault(pair.Value, fallback);
    }

    private IEnumerable<PointF> GetHeapOffsets(KnownObject basis, KnownObject piece)
    {
        var pair = FamilyPair.FromPieces(basis, piece);
        if (pair is not null && _grammar.Heap.Touch.TryGetValue(pair.Value, out var offsets))
        {
            int sign = basis.Family == pair.Value.First ? 1 : -1;
            return offsets.Select(offset => new PointF(sign * offset.X, sign * offset.Y));
        }

        float touch = (GetExtent(basis) + GetExtent(piece)) / 2;
        return [new PointF(touch, 0), new PointF(-touch, 0), new PointF(0, touch), new PointF(0, -touch)];
    }

    private KnownObject? Pick(PieceFamily? family)
    {
        if (family is null)
        {
            return null;
        }

        if (!_palette.TryGetValue(family.Value, out var palette))
        {
            var rule = _grammar.Families.GetValueOrDefault(family.Value);
            var weights = rule is null ? [] : _campaign ? rule.CampaignIds : rule.MainIds;
            var main = Draw(weights);
            var second = rule is null || _rng.NextSingle() >= rule.MixShare ? null : Draw(weights.Where(pair => pair.Key != main).ToDictionary(pair => pair.Key, pair => pair.Value));
            _palette[family.Value] = palette = (main, second, rule?.MinorityShare ?? 0);
        }

        return palette.Second is not null && _rng.NextSingle() < palette.Share ? palette.Second : palette.Main;
    }

    private KnownObject? Draw(Dictionary<KnownObject, float> weights)
    {
        var allowed = weights.Where(pair => !_campaign || !_grammar.AddOnOnly.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return allowed.Count == 0 ? null : Draw<KnownObject>(allowed);
    }

    private byte GetFacing(KnownObject id, PointF point)
    {
        int faces = _room.Faces.GetValueOrDefault(_room.GetCell(point));
        var byFace = _grammar.Facings.GetValueOrDefault(id) ?? [];
        int face = new[] { PlusU, MinusV, MinusU, PlusV }.FirstOrDefault(face => (faces & face) != 0 && byFace.ContainsKey(face));
        return DrawFacing(id, byFace.GetValueOrDefault(face) ?? []);
    }

    // A signature's first module keeps to facings that carry companions
    private byte DrawFacing(KnownObject id, Dictionary<byte, float> facings)
    {
        var modules = GetModules(id);
        var kept = facings.Where(pair => modules.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        return _force && kept.Count > 0 ? Draw(kept) : facings.Count > 0 ? Draw(facings) : (byte)0;
    }

    private Dictionary<byte, Companion[]> GetModules(KnownObject id) =>
        _program.Companions.GetValueOrDefault(id) ?? _grammar.SharedModules.GetValueOrDefault(id) ?? [];

    private float Draw(float[] range) => range[0] + _rng.NextSingle() * (range[1] - range[0]);

    private T Draw<T>(Dictionary<T, float> weights)
        where T : notnull => weights.Keys.ElementAt(DrawIndex(weights.Values.ToArray()));

    private int DrawIndex(float[] weights)
    {
        float pick = _rng.NextSingle() * weights.Sum();
        for (int index = 0; index < weights.Length - 1; index++)
        {
            pick -= weights[index];
            if (pick < 0)
            {
                return index;
            }
        }

        return weights.Length - 1;
    }
}
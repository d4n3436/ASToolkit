using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using static ASToolkit.Core.Generation.Dressing.FurnitureGrammar;
using static ASToolkit.Core.Generation.Dressing.RoomGeometry;

namespace ASToolkit.Core.Generation.Dressing;

internal sealed class SceneFurnisher
{
    private const float MinStep = 0.5f;
    private const int Tries = 12;
    private const int GrowTries = 40;
    private const int SceneTries = 48;
    private const float OpeningReach = 3;
    private const float SquareRatio = 1.25f;
    private const float MiddleSlack = 1.5f;
    private const float SeatSlack = 0.3f;
    private const float SeatReach = 0.7f;
    private const float FrontSpan = 60;
    private static readonly RoomProgram EmptyProgram = new([[0, 0], [0, 0], [0, 0], [0, 0]], false, 0, [], []);

    private readonly FurnitureGrammar _grammar;
    private readonly ObjectTable _objects;
    private readonly bool _campaign;
    private readonly Random _rng;
    private readonly FineLayout _layout;
    private readonly LevelOccupancy _occupancy;
    private readonly IReadOnlySet<Cell> _reserved;
    private readonly WallClearanceTable _clearance;
    private readonly float _sizeShare;
    private readonly bool _rubble;
    private readonly WallSet _wallSet;
    private readonly List<DecorObject> _placed = [];

    private RoomGeometry _room = null!;
    private RoomProgram _program = null!;
    private HashSet<Cell> _blocked = [];
    private List<(PointF Point, KnownObject Id, byte Dir, RectangleF Box)> _floor = [];
    private Dictionary<PieceFamily, (KnownObject? Main, KnownObject? Second, float Share)> _palette = [];
    private Dictionary<Arrangement, int> _placedCounts = [];
    private int _anchors;
    private bool _force;
    private bool _inScene;

    public SceneFurnisher(FurnitureGrammar grammar, ObjectTable objects, GenerationStyle style, Random rng, FineLayout layout, LevelOccupancy occupancy,
        IReadOnlySet<Cell> reserved, WallSet wallSet, WallClearanceTable clearance, bool rubble)
    {
        _wallSet = wallSet;
        _rubble = rubble;
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

    public List<DecorObject> Furnish(LayoutPlan plan, IReadOnlyList<SceneRequest> scenes)
    {
        var held = scenes.ToList();
        var done = new HashSet<int>();
        foreach (var region in plan.Regions.OrderBy(region => scenes.Any(scene => scene.Region == region.Id) ? 0 : 1))
        {
            var type = region.IsHall ? RoomType.Hall : region.Type ?? RoomType.Passage;
            type = _grammar.Aliases.GetValueOrDefault(type, type);
            var own = held.Where(scene => scene.Region == region.Id).ToArray();
            bool furnished = region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.SecretRoom && _grammar.Types.ContainsKey(type);
            done.Add(region.Id);
            if (!furnished && own.Length == 0)
                continue;

            var room = new RoomGeometry(_layout, _layout.GetRegionCells(region.Id), _reserved);
            foreach (var scene in FurnishRoom(room, furnished ? _grammar.Types[type] : EmptyProgram, own, furnished ? type : null))
            {
                int[] next = scene.Fallbacks.SkipWhile(done.Contains).ToArray();
                if (next.Length > 0)
                {
                    held.Add(scene with { Region = next[0], Fallbacks = next[1..] });
                }
            }
        }

        AddGibs();
        return _placed;
    }

    private List<SceneRequest> FurnishRoom(RoomGeometry room, RoomProgram program, SceneRequest[] scenes, RoomType? type)
    {
        _room = room;
        _program = program;
        _blocked = [];
        _floor = [];
        _palette = [];
        _placedCounts = [];
        _anchors = 0;
        _inScene = true;
        var lined = scenes.Where(scene => scene.At is null && _grammar.Scenes[scene.Anchor].Kind == ArrangementKind.Lined).ToArray();
        var missed = scenes.Except(lined).Where(scene => !StandScene(scene))
            .Concat(lined.GroupBy(scene => scene.Anchor).SelectMany(group => group.Skip(PlaceLined(group.Key, _grammar.Scenes[group.Key].Lining!, group.Count()))))
            .ToList();

        _inScene = false;

        float target = Draw(program.PiecesPerHundredCells[GetBand(DensityBands)]) * room.HalfCells / 100;
        bool big = room.HalfCells >= BigRoomCells;
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

        if (type is not null)
        {
            Dress(type.Value);
        }

        return missed;
    }

    private void Dress(RoomType type)
    {
        foreach (var fixture in _grammar.Fixtures)
        {
            float share = fixture.Shares.GetValueOrDefault(type);
            foreach (var wall in _room.Walls.Where(wall => (wall.Face & FarFaces) != 0))
            {
                foreach (var cell in wall.Cells.Where(cell => !_room.Openings.Contains(cell) && _rng.NextSingle() < share))
                {
                    Hang(fixture, cell, wall.Face, Along(cell, wall.Face) + 0.5f);
                }
            }
        }

        var victims = _grammar.Victims.GetValueOrDefault(type);
        if (victims is not null)
        {
            PlaceVictims(victims);
        }

        var rubble = _grammar.Rubble;
        var cells = _room.Cells.ToArray();
        for (int pile = _rubble ? (int)MathF.Floor(rubble.PerHundredCells.GetValueOrDefault(type) * _room.HalfCells / 100 + _rng.NextSingle()) : 0; pile > 0; pile--)
        {
            bool heap = _rng.NextSingle() < rubble.BigShare;
            var cell = cells[_rng.Next(cells.Length)];
            Splash(KnownObject.LyingRockDebris, new PointF(cell.U + _rng.NextSingle(), cell.V + _rng.NextSingle()), heap ? rubble.Big : rubble.Small, heap ? rubble.BigRadius : rubble.SmallRadius,
                rubble.Z);
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
                if (bare != BareWallCells)
                    continue;

                var stretch = cells[(index + 1 - BareWallCells)..(index + 1)];
                if (cover.Kind == ArrangementKind.Heap)
                {
                    PlaceHeap(0, 0, (face, stretch[BareWallCells / 2]));
                }
                else
                {
                    PlaceRun(face, stretch, Along(stretch[0], face) + 0.5f, cover.Family!.Value, cover.Members, BareWallCells);
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

    private int PlaceLined(KnownObject id, Lining lining, int count)
    {
        var placed = new List<LinedSpot>();
        while (placed.Count < count)
        {
            var stood = GetTiers().Select(Stand).FirstOrDefault(spots => spots.Length > 0, []);
            if (stood.Length == 0)
                break;

            placed.AddRange(stood);
        }

        return placed.Count;

        IEnumerable<LinedPair[]> GetTiers()
        {
            var spots = GetLinedSpots(id, lining, true);
            var free = count - placed.Count < 2 ? [] : spots.Where(spot => IsFree(GetBox(id, spot.Point))).ToArray();
            yield return free
                .SelectMany(first => free.Where(second => IsPair(first, second) && IsMatching(first, second)).Select(second => new LinedPair(first, second, first.Weight * second.Weight)))
                .ToArray();

            yield return spots.Select(spot => new LinedPair(spot, null, spot.Weight)).ToArray();
            yield return GetLinedSpots(id, lining, false)
                .Where(spot => placed.All(other => GetSharedSide(other, spot) == 0))
                .Select(spot => new LinedPair(spot, null, spot.Weight))
                .ToArray();
        }

        LinedSpot[] Stand(LinedPair[] tier)
        {
            var left = tier.ToList();
            while (left.Count > 0)
            {
                var chosen = left[DrawIndex(left.Select(pair => pair.Weight).ToArray())];
                if (PlaceModule(id, chosen.First.Point, chosen.First.Dir))
                {
                    var second = chosen.Second;
                    return second is not null && PlaceModule(id, second.Value.Point, second.Value.Dir) ? [chosen.First, second.Value] : [chosen.First];
                }

                left.Remove(chosen);
            }

            return [];
        }

        bool IsMatching(LinedSpot first, LinedSpot second) =>
            GetModules(id)[first.Dir].Length == GetModules(id)[second.Dir].Length && !GetBox(id, first.Point).IntersectsWith(GetBox(id, second.Point));
    }

    private static int GetSharedSide(LinedSpot first, LinedSpot second) =>
        new[] { PlusU, MinusV, MinusU, PlusV }.FirstOrDefault(face => (first.Faces & second.Faces & face) != 0 && Across(first.Cell, face) == Across(second.Cell, face));

    private static bool IsPair(LinedSpot first, LinedSpot second)
    {
        int shared = GetSharedSide(first, second);
        return shared != 0 && (first.Faces ^ second.Faces) == (shared is PlusU or MinusU ? MinusV | PlusV : PlusU | MinusU)
            || (first.Faces | second.Faces) is (PlusU | MinusU) or (MinusV | PlusV) && MathF.Abs(first.Along - second.Along) < MiddleSlack;
    }

    private LinedSpot[] GetLinedSpots(KnownObject id, Lining lining, bool middles)
    {
        var sides = new[] { PlusU, MinusV, MinusU, PlusV }
            .SelectMany(face => _room.Cells
                .Where(cell => (_room.Faces[cell] & face) != 0 && !_room.Pillars.Contains(GetBeyond(cell, face)))
                .GroupBy(cell => Across(cell, face))
                .Select(side => new WallLine(face, side.OrderBy(cell => Along(cell, face)).ToArray())))
            .ToArray();

        if (sides.Length == 0)
        {
            return [];
        }

        int shortest = sides.Min(GetSpan);
        var spots = new List<LinedSpot>();
        foreach ((int mask, var dirs) in _grammar.Facings.GetValueOrDefault(id) ?? [])
        {
            int[] walls = new[] { mask & (PlusU | MinusU), mask & (MinusV | PlusV) }.Where(wall => wall != 0).ToArray();
            var places = walls.Length == 2
                ? _room.Cells.Where(cell => (_room.Faces[cell] & mask) == mask && walls.All(wall => !_room.Pillars.Contains(GetBeyond(cell, wall))) && walls.All(wall => IsEnd(wall, cell)) == middles)
                    .Select(cell => (Cell: cell, Along: 0f))
                : sides.Where(side => side.Face == mask && IsEnd(mask, side.Cells[0])).SelectMany(side => GetPlaces(side, middles));

            foreach ((var cell, float along) in places)
            {
                var at = walls.Aggregate(new PointF(along, along), (point, wall) => wall is PlusU or MinusU
                    ? point with { X = FromWall(cell, wall, GetLiningDistance(lining, wall), 0).X }
                    : point with { Y = FromWall(cell, wall, GetLiningDistance(lining, wall), 0).Y });

                float weight = walls.Min(wall => MathF.Pow((float)shortest / GetSideSpan(wall, cell), 4));
                spots.AddRange(dirs
                    .Select(pair => (Dir: pair.Key, Share: pair.Value, First: GetModules(id).GetValueOrDefault(pair.Key, []).FirstOrDefault(piece => piece.Id == lining.Id && piece.Slot == 0)))
                    .Where(entry => entry.First is not null)
                    .Select(entry => new LinedSpot(new PointF(at.X - entry.First!.U, at.Y - entry.First.V), entry.Dir, entry.Share * weight, cell, mask, along)));
            }
        }

        return spots.Where(spot => IsClearLined(id, lining, spot)).ToArray();

        bool IsEnd(int face, Cell cell) => GetSideSpan(face, cell) <= SquareRatio * (_room.Cells.Max(other => Across(other, face)) - _room.Cells.Min(other => Across(other, face)) + 1);
        int GetSideSpan(int face, Cell cell) => GetSpan(sides.First(side => side.Face == face && Across(side.Cells[0], face) == Across(cell, face)));

        IEnumerable<(Cell Cell, float Along)> GetPlaces(WallLine side, bool middle)
        {
            // Shift the middle toward the far end, low on v and high on u, to center it on screen
            float shift = (lining.NearDistance - lining.FarDistance) / 2;
            float center = (Along(side.Cells[0], side.Face) + Along(side.Cells[^1], side.Face) + 1) / 2f + (side.Face is PlusU or MinusU ? -shift : shift);
            return middle
                ? [(side.Cells.MinBy(cell => MathF.Abs(Along(cell, side.Face) + 0.5f - center)), center)]
                : side.Cells.Select(cell => (cell, Along(cell, side.Face) + 0.5f));
        }
    }

    private bool IsClearLined(KnownObject id, Lining lining, LinedSpot spot)
    {
        var pads = GetModules(id)[spot.Dir].Where(piece => piece.Id == lining.Id).ToArray();
        var first = pads.First(piece => piece.Slot == 0);
        var points = pads.Select(pad => new PointF(spot.Point.X + pad.U, spot.Point.Y + pad.V)).Append(spot.Point);
        return !points.Any(point => _room.Openings.Any(cell => GetDistance(point, new PointF(cell.U + 0.5f, cell.V + 0.5f)) < OpeningReach))
            && pads.All(pad => new[] { PlusU, MinusV, MinusU, PlusV }.Where(wall => (spot.Faces & wall) != 0).All(wall =>
            {
                var normal = GetNormal(wall);
                float reach = GetLiningDistance(lining, wall) - 0.5f;
                var probe = new PointF(spot.Point.X + pad.U + normal.X * reach, spot.Point.Y + pad.V + normal.Y * reach);
                return MathF.Abs((pad.U - first.U) * normal.X + (pad.V - first.V) * normal.Y) > 0.01f || (_room.Faces.GetValueOrDefault(_room.GetCell(probe)) & wall) != 0;
            }));
    }

    private static int GetSpan(WallLine side) => Along(side.Cells[^1], side.Face) - Along(side.Cells[0], side.Face) + 1;

    private static float GetDistance(PointF first, PointF second) => MathF.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    private static float GetLiningDistance(Lining lining, int face) => (face & FarFaces) != 0 ? lining.FarDistance : lining.NearDistance;

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

    private bool StandScene(SceneRequest scene)
    {
        if (scene.At is null)
        {
            var arrangement = _grammar.Scenes[scene.Anchor];
            int before = _anchors;
            bool first = _floor.All(piece => piece.Id != scene.Anchor);
            for (int attempt = 0; attempt < SceneTries && _anchors == before; attempt++)
            {
                Place(arrangement, true);
            }

            if (_anchors != before || !first || _room.Interior.Length <= 0)
                return _anchors > before;
            
            _force = true;
            PlaceGrid(arrangement.Family!.Value, 1);
            _force = false;

            return _anchors > before;
        }

        (float u, float v) = _layout.Lattice.ToLattice(scene.At.Value);
        var point = new PointF(u, v);
        _floor.Add((point, scene.Anchor, scene.Dir, RectangleF.Empty));
        _force = true;
        StandCompanions(scene.Anchor, point, scene.Dir);
        _force = false;
        return true;
    }

    private bool PlaceModule(KnownObject id, PointF point, byte dir, int lining = 0)
    {
        var box = GetBox(id, point, lining);
        if (!IsInside(id, point, dir, lining) || !IsFree(box) || !Commit(id, point, 0, dir, lining))
        {
            return false;
        }

        _floor[^1] = (point, id, dir, box);
        _anchors++;
        StandCompanions(id, point, dir);

        _force = false;
        return true;
    }

    private RectangleF GetBox(KnownObject id, PointF point, int lining = 0)
    {
        float extent = GetExtent(id), depth = lining == 0 ? extent : MathF.Min(extent, 2 * (GetRule(id)?.WallDistance ?? MinStep));
        float spanU = lining is PlusU or MinusU ? depth : extent, spanV = lining is PlusU or MinusU ? extent : depth;
        return new RectangleF(point.X - spanU / 2, point.Y - spanV / 2, spanU, spanV);
    }

    private bool IsHidden(PointF point) => _floor.Any(piece => IsInFront(piece.Point, point));

    private bool IsInFront(PointF front, PointF behind)
    {
        var near = _layout.Lattice.ToWorld(front.X, front.Y);
        var far = _layout.Lattice.ToWorld(behind.X, behind.Y);
        return near.Y > far.Y && MathF.Abs(near.X - far.X) < FrontSpan && GetDistance(front, behind) < _grammar.Splatter.FrontReach;
    }

    private float GetWallReach(PointF point, WallSides side)
    {
        var hit = _layout.Edges.CastRay(point.X, point.Y, side, _layout.Cells.ContainsKey, 3);
        return hit is not null && hit.Value.Kind.ShowsWall ? hit.Value.Distance : float.PositiveInfinity;
    }

    private bool IsFree(RectangleF box) => _floor.All(piece => !Overlaps(piece.Box, box));

    private static bool Overlaps(RectangleF first, RectangleF second) => RectangleF.Intersect(first, second) is { Width: > 0.02f, Height: > 0.02f };

    private void StandCompanions(KnownObject id, PointF point, byte dir)
    {
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
    }

    private bool Commit(KnownObject id, PointF point, float z, byte dir, int lining = -1)
    {
        var world = _layout.Lattice.ToWorld(point.X, point.Y);
        var cell = _room.GetCell(point);
        var footprint = z > 0 ? [] : CollisionBox.GetFootprint(_layout.Lattice, _objects, id, world.X, world.Y);
        footprint.IntersectWith(_room.Cells);

        // Keep pieces walked over or through out of the spacing and the floor list
        bool standing = z <= 0 && !id.IsAttached && (id.Family is not null || _objects.GetBlocking(id) == BlockingClass.Solid);
        if (_campaign && _grammar.AddOnOnly.Contains(id) || !_room.Cells.Contains(cell) || standing && lining < 0 && !IsInside(id, point, dir, 0) || _reserved.Contains(cell) && (standing || !_inScene)
            || footprint.Overlaps(_room.Openings) || standing && _floor.Any(other => IsTooClose(other, id, point, dir, lining >= 0)) || !IsJoinedWithout(footprint)
            || standing && _placed.Any(piece => piece.Id.IsCorpse && IsInFront(point, new PointF(piece.U, piece.V)))
            || !_occupancy.Fits(id, world, z, _inScene))
        {
            return false;
        }

        _blocked.UnionWith(footprint);
        if (standing)
        {
            _floor.Add((point, id, dir, RectangleF.Empty));
        }

        _occupancy.Add(id, world, z);

        // Keep the floor around a set piece's walked-over pieces clear of the furniture after it
        if (_inScene && _objects.GetBlocking(id) != BlockingClass.Solid)
        {
            _occupancy.Reserve(Enumerable.Range(-1, 3).SelectMany(du => Enumerable.Range(-1, 3).Select(dv => new Cell(cell.U + du, cell.V + dv))));
        }

        byte[] payload = id switch
        {
            KnownObject.Crate => KnownObject.CreateEmptyCratePayload(),
            KnownObject.TeleportPad => [0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0],
            _ => []
        };

        // Keep corpses clear of near walls and of pieces in front of them on screen, and lay a corpse with nothing behind its seated picture flat
        if (id.IsCorpse && (new[] { WallSides.LowerBack, WallSides.LowerSlash }.Any(side => GetWallReach(point, side) < _grammar.Splatter.NearWallReach) || IsHidden(point)))
        {
            return false;
        }

        var pose = _grammar.Poses.GetValueOrDefault(id);
        var seat = pose is not null && _objects.TryFind(id, out var corpse)
            ? pose.Seats.GetValueOrDefault((byte)(corpse.GetDirection(dir) * 256 / corpse.Directions))
            : null;
        if (pose is not null && seat is not null && !IsSeated(pose, seat, point))
        {
            dir = pose.DrawLying(_rng);
        }

        Add(id, point, z, dir, payload);
        if (id.IsCorpse)
        {
            Add(KnownObject.BloodPool, point, 0, (byte)_rng.Next(256));
            Splash(KnownObject.SmallBloodPool, point, _grammar.Splatter.CorpseBlood, _grammar.Splatter.CorpseRadius);
        }

        foreach (var attached in _grammar.Attached.GetValueOrDefault(id) ?? [])
        {
            var at = new PointF(point.X + attached.U, point.Y + attached.V);
            if (_rng.NextSingle() < attached.Share)
            {
                Commit(attached.Id, at, 0, _grammar.Facings.ContainsKey(attached.Id) ? GetFacing(attached.Id, at) : dir);
            }
        }

        return true;
    }

    private void Add(KnownObject id, PointF point, float z, byte dir, byte[]? payload = null) =>
        _placed.Add(new DecorObject(id, point.X, point.Y, z, dir, payload ?? [], null));

    private void Hang(Fixture fixture, Cell wall, int face, float along)
    {
        var id = Draw(fixture.Ids);
        if (id is not null)
        {
            Commit(id.Value, FromWall(wall, face, fixture.WallDistance, along), fixture.Z, fixture.Dirs[face]);
        }
    }

    private void PlaceVictims(VictimRule rule)
    {
        var splatter = _grammar.Splatter;
        var beside = _floor
            .Where(piece => rule.Beside.Length == 0 || rule.Beside.Any(family => piece.Id.Family == family))
            .Select(piece => piece.Point)
            .ToArray();

        var cells = _room.Cells.ToArray();
        int count = (int)MathF.Floor(rule.PerHundredCells * _room.HalfCells / 100 + _rng.NextSingle())
                    - _placed.Count(piece => piece.Id.IsCorpse && _room.Cells.Contains(_room.GetCell(new PointF(piece.U, piece.V))));
        for (int unit = 0; unit < count; unit++)
        {
            bool corpse = _rng.NextSingle() < splatter.CorpseShare;
            var id = _rng.NextSingle() < rule.ScientistShare ? KnownObject.ScientistCorpse : KnownObject.SoldierCorpse;
            var pose = _grammar.Poses[id];
            byte dir = Draw(pose.Pictures);
            var seat = pose.Seats.GetValueOrDefault(dir);
            for (int attempt = 0; attempt < Tries; attempt++)
            {
                float angle = _rng.NextSingle() * 2 * MathF.PI, reach = Draw(splatter.Reach);
                var cell = cells[_rng.Next(cells.Length)];
                var basis = beside.Length > 0 ? beside[_rng.Next(beside.Length)] : new PointF(cell.U + 0.5f, cell.V + 0.5f);
                var point = beside.Length > 0 ? new PointF(basis.X + MathF.Cos(angle) * reach, basis.Y + MathF.Sin(angle) * reach) : basis;
                int partner = -1;
                if (corpse && seat is not null)
                {
                    var seated = GetSeat(pose, seat);
                    if (seated is null || !IsSeated(pose, seat, seated.Value.Point))
                        continue;

                    (point, partner) = (seated.Value.Point, seated.Value.Partner);
                }

                // Let a seated corpse touch only the piece it leans on, and keep every corpse off the others' footprints
                var box = GetBox(id, point);
                bool free = _floor
                    .Select((piece, index) => index == partner ? !piece.Box.Contains(point) : !Overlaps(piece.Box, box) && GetDistance(piece.Point, point) >= GetExtent(piece.Id) / 2)
                    .All(clear => clear);
                if (corpse && (!free || _placed.Any(piece => piece.Id.IsCorpse && GetDistance(new PointF(piece.U, piece.V), point) < splatter.Gap)))
                    continue;

                if (corpse ? Commit(id, point, 0, dir) : IsFloor(point) && Splash(KnownObject.SmallBloodPool, point, splatter.Blood, splatter.Radius) > 0)
                    break;
            }
        }
    }

    private SeatSpot? GetSeat(CorpsePose pose, Seat seat)
    {
        var pieces = Enumerable.Range(0, _floor.Count)
            .Where(index => GetFaceOffsets(pose, seat, _floor[index].Id, _floor[index].Dir).Length > 0)
            .ToArray();

        if (pieces.Length > 0 && _rng.NextSingle() < pose.PieceShare)
        {
            int index = pieces[_rng.Next(pieces.Length)];
            var piece = _floor[index];
            var offsets = GetFaceOffsets(pose, seat, piece.Id, piece.Dir);
            return new SeatSpot(FromPartner(piece.Point, seat, offsets[_rng.Next(offsets.Length)]), index);
        }

        var walls = _room.Walls.Where(wall => wall.Face == seat.Face).ToArray();
        if (walls.Length == 0)
        {
            return null;
        }

        var cells = walls[_rng.Next(walls.Length)].Cells;
        var cell = cells[_rng.Next(cells.Length)];
        return new SeatSpot(FromWall(cell, seat.Face, seat.WallDistance[_wallSet], Along(cell, seat.Face) + _rng.NextSingle()), -1);
    }

    // Take the offsets for the face the partner turns to the seat, keeping those in front of it, never past its corner
    private PointF[] GetFaceOffsets(CorpsePose pose, Seat seat, KnownObject id, byte dir)
    {
        var offsets = pose.Partners.FirstOrDefault(pair => id.Family == pair.Key).Value;
        if (offsets is null || !_objects.TryFind(id, out var partner))
        {
            return [];
        }

        int picture = partner.GetDirection(dir);
        var face = !offsets.Pictures.Contains(picture) ? [] : picture == seat.Picture ? offsets.Same : offsets.Turned;
        return face.Where(offset => MathF.Abs(offset.Y) < GetExtent(id) / 2).ToArray();
    }

    private static PointF FromPartner(PointF partner, Seat seat, PointF offset)
    {
        var normal = GetNormal(seat.Face);
        return new PointF(partner.X - normal.X * offset.X - normal.Y * offset.Y, partner.Y - normal.Y * offset.X - normal.X * offset.Y);
    }

    // Wall sides share the face bits
    private bool IsSeated(CorpsePose pose, Seat seat, PointF point)
    {
        var wall = _layout.Edges.FindNearestWall(point.X, point.Y);
        var normal = GetNormal(seat.Face);
        bool between = wall is not null && _floor.Any(piece =>
        {
            float along = (piece.Point.X - point.X) * normal.X + (piece.Point.Y - point.Y) * normal.Y;
            float across = (piece.Point.X - point.X) * normal.Y + (piece.Point.Y - point.Y) * normal.X;
            return along > 0 && along < wall.Value.Distance && MathF.Abs(across) <= SeatReach;
        });

        return wall is not null && !between && (int)wall.Value.Side == seat.Face && MathF.Abs(wall.Value.Distance - seat.WallDistance[_wallSet]) <= SeatSlack
               || _floor.Any(piece => GetFaceOffsets(pose, seat, piece.Id, piece.Dir).Any(offset => GetDistance(FromPartner(piece.Point, seat, offset), point) <= SeatSlack));
    }

    private int Splash(KnownObject id, PointF center, int[] count, float radius, float z = 0)
    {
        int splashed = 0;
        for (int drop = _rng.Next(count[0], count[1] + 1); drop > 0; drop--)
        {
            float angle = _rng.NextSingle() * 2 * MathF.PI, distance = radius * _rng.NextSingle();
            var point = new PointF(center.X + MathF.Cos(angle) * distance, center.Y + MathF.Sin(angle) * distance);
            if (!IsFloor(point))
                continue;
            
            Add(id, point, z, (byte)_rng.Next(256));
            splashed++;
        }

        return splashed;
    }

    private bool IsFloor(PointF point) => _room.Cells.Contains(_room.GetCell(point)) && !_reserved.Contains(_room.GetCell(point));

    private void AddGibs()
    {
        var splatter = _grammar.Splatter;
        var left = _placed
            .Where(piece => piece.Id == KnownObject.SmallBloodPool)
            .Select(piece => new PointF(piece.U, piece.V))
            .ToList();

        while (left.Count > 0)
        {
            var cluster = new List<PointF> { left[^1] };
            left.RemoveAt(left.Count - 1);
            for (int index = 0; index < cluster.Count; index++)
            {
                var seed = cluster[index];
                cluster.AddRange(left.Where(point => GetDistance(point, seed) < splatter.GibReach));
                left.RemoveAll(point => GetDistance(point, seed) < splatter.GibReach);
            }

            for (int gib = cluster.Count >= splatter.GibBlood ? cluster.Count / splatter.BloodPerGib : 0; gib > 0; gib--)
            {
                Add(KnownObject.FlyingGibs, cluster[_rng.Next(cluster.Count)], 0, (byte)_rng.Next(256));
            }
        }
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

internal readonly record struct SceneRequest(int Region, KnownObject Anchor, PointF? At, byte Dir, int[] Fallbacks);

internal readonly record struct LinedSpot(PointF Point, byte Dir, float Weight, Cell Cell, int Faces, float Along);

// No partner is index -1
internal readonly record struct SeatSpot(PointF Point, int Partner);

internal readonly record struct LinedPair(LinedSpot First, LinedSpot? Second, float Weight);

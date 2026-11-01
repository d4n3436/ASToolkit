using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Design;

/// <summary>
/// Provides the design measures of a level from its map model, route and survey.
/// </summary>
public sealed class DesignMeasurer
{
    private const float TableReach = 60;
    private const float BigComputerReach = 200;
    private const float BloodReach = 40;
    private const float PileReach = 45;
    private const float GroupReach = 70;
    private const float MissileReach = 120;
    private const float PadReach = 400;
    private const float MirrorTolerance = 25;
    private const float StripeReach = 230;
    private const float FightReach = 1200;
    private const float BigFightShare = 0.05f;
    private const float EarlyShare = 0.2f;
    private const float ScreenHalfWidth = 640;
    private const float ScreenHalfHeight = 400;
    private const float FloorHeight = 5;
    private const int RepeatMinObjects = 8;
    private const int RepeatMinMatches = 6;
    private const float RepeatTolerance = 4;
    private const float StorageDominance = 0.6f;
    private const float DoorwayReach = 30;
    private const float MonitorDoorwayReach = 40;
    private const float MonitorFurnitureReach = 25;
    private const float InsideShare = 0.12f;
    private const int OffRouteWalk = 200;

    private static readonly HashSet<KnownObject> Computers = [KnownObject.Computer, KnownObject.ComputerDead];
    private static readonly HashSet<KnownObject> Pads = [KnownObject.TeleportPad, KnownObject.SilentTeleportPad];
    private static readonly HashSet<KnownObject> TallFurniture = [KnownObject.Cabinet, KnownObject.Shelves];
    private static readonly HashSet<KnownObject> BoxSupports = [KnownObject.Shelves, KnownObject.Cabinet, KnownObject.Table];
    private static readonly HashSet<KnownObject> DeskTops = [KnownObject.TestTubes, KnownObject.LabEquipment, KnownObject.Computer, KnownObject.ComputerDead];
    private static readonly HashSet<KnownObject> OfficeDecor = [KnownObject.Computer, KnownObject.ComputerDead, KnownObject.TestTubes, KnownObject.LabEquipment, KnownObject.ScatteredPaper];

    private static readonly HashSet<KnownObject> Furniture =
    [
        KnownObject.Table, KnownObject.Chair, KnownObject.ScatteredPaper, KnownObject.Cabinet, KnownObject.Monitor, KnownObject.MonitorDead, KnownObject.WaterCylinder,
        KnownObject.Plant, KnownObject.Computer, KnownObject.ComputerDead, KnownObject.Telephone, KnownObject.NewChair, KnownObject.TestTubes, KnownObject.LabEquipment,
        KnownObject.ExperimentFlasks, KnownObject.RadioactiveFlask, KnownObject.BioContainer, KnownObject.BigComputer, KnownObject.RadioactiveBarrel
    ];

    private static readonly HashSet<KnownObject> Storage =
    [
        KnownObject.Crate, KnownObject.Barrel, KnownObject.Container, KnownObject.Cylinder, KnownObject.Shelves, KnownObject.Boxes, KnownObject.Truck,
        KnownObject.Container2, KnownObject.Container2Alt, KnownObject.Missile
    ];

    private static readonly HashSet<KnownObject> Unrepeated =
    [
        KnownObject.BloodPool, KnownObject.SmallBloodPool, KnownObject.LargeBloodPool, KnownObject.VerticalBloodPool, KnownObject.EmbeddedShadow,
        KnownObject.WallInscriptions, KnownObject.FlyingGibs, KnownObject.LyingGibs, KnownObject.BlastMark, KnownObject.LyingRockDebris, KnownObject.RockDebris
    ];

    private readonly LevelModel _model;
    private readonly RouteAnalysis _analysis;
    private readonly LevelSurvey _survey;
    private readonly ObjectTable _objects;
    private readonly ILookup<KnownObject, PointF> _positions;
    private readonly Lazy<HashSet<int>> _storagePlaces;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesignMeasurer"/> class.
    /// </summary>
    /// <param name="model">The level model.</param>
    /// <param name="analysis">The route analysis.</param>
    /// <param name="survey">The survey of the level.</param>
    /// <param name="objects">The game objects.</param>
    public DesignMeasurer(LevelModel model, RouteAnalysis analysis, LevelSurvey survey, ObjectTable objects)
    {
        _model = model;
        _analysis = analysis;
        _survey = survey;
        _objects = objects;
        _positions = model.Sprites.ToLookup(sprite => sprite.Id, sprite => new PointF(sprite.Position.X, sprite.Position.Y));
        _storagePlaces = new Lazy<HashSet<int>>(FindStoragePlaces);
    }

    /// <summary>
    /// Measures a level.
    /// </summary>
    public static DesignMeasures Measure(LevelModel model, RouteAnalysis analysis, LevelSurvey survey, ObjectTable objects) =>
        new DesignMeasurer(model, analysis, survey, objects).Measure();

    /// <summary>
    /// Measures the level.
    /// </summary>
    public DesignMeasures Measure()
    {
        var walls = _model.Sprites
            .Where(sprite => sprite.Position.Z < FloorHeight && _model.Cells.Contains(_model.Lattice.GetCell(sprite.Position.X, sprite.Position.Y)))
            .Select(sprite => GetWallSample(sprite.Id, sprite.Facing, sprite.Position.X, sprite.Position.Y))
            .ToArray();

        var tables = Find(KnownObject.Table);
        var chairs = Find(id => id.IsChair);
        var loose = _survey.Items.Where(item => !item.InCrate).ToArray();
        var cannon = Find(KnownObject.MountedCannon).Select(point => (PointF?)point).FirstOrDefault();
        var doorways = GetDoorways();
        var floorPieces = _model.Objects.Where(piece => piece.Z < FloorHeight).ToArray();
        var blocking = floorPieces.Where(piece => _objects.GetBlocking(piece.Id) != BlockingClass.WalkThrough).ToArray();
        var monitors = _model.Objects.Where(piece => piece.Id.IsWallMounted).ToArray();
        var boxes = _model.Objects.Where(piece => piece.Id == KnownObject.Boxes).ToArray();
        var spawners = _analysis.Elements.Where(element => element.Kind == ElementKind.Spawner && element.Program is not null).Select(element => element.Program!).ToArray();
        var deskTops = _model.Objects.Where(piece => DeskTops.Contains(piece.Id)).ToArray();
        return new DesignMeasures
        {
            Signature = _survey.Signature,
            ChairsAtTables = CountNear(chairs, tables, TableReach),
            TablesWithChairs = CountNear(tables, chairs, TableReach),
            TubesOnTables = CountNear(Find(KnownObject.TestTubes), tables, TableReach),
            LabEquipmentOnTables = CountNear(Find(KnownObject.LabEquipment), tables, TableReach),
            PaperOnTables = CountNear(Find(KnownObject.ScatteredPaper), tables, TableReach),
            ComputersAwayFromWalls = CountAwayFromWalls(Computers.SelectMany(Find).ToArray()),
            BigComputersWithChairs = _model.Theme == LevelTheme.Lab ? CountNear(Find(KnownObject.BigComputer), chairs, BigComputerReach) : default,
            CorpsesOnBlood = CountNear(Find(id => id.IsCorpse), Find(id => id.IsBlood), BloodReach),
            OfficeDecorInPassages = CountInPlaces(place => place.Kind is PlaceKind.Hall or PlaceKind.Corridor or PlaceKind.Yard or PlaceKind.Nook),
            OfficeDecorInStorage = CountInPlaces(IsStorage),
            Walls = walls,
            MoneyAgainstWalls = CountItems(loose, ItemCategory.Money, sample => sample.Sides != WallSides.None),
            MoneyInCorners = CountItems(loose, ItemCategory.Money, sample => sample.Corner),
            HealthAgainstWalls = CountItems(loose, ItemCategory.Health, sample => sample.Sides != WallSides.None),
            AmmoAgainstWalls = CountItems(loose, ItemCategory.Ammo, sample => sample.Sides != WallSides.None),
            ArmorAgainstWalls = CountItems(loose, ItemCategory.Armor, sample => sample.Sides != WallSides.None),
            PickupPiles = CountPiles(loose),
            HealthAfterFights = new MeasureShare(_survey.Items.Count(item => item is { Category: ItemCategory.Health, After: not null }),
                _survey.Items.Count(item => item.Category == ItemCategory.Health)),
            ShelvesGrouped = CountGrouped(Find(KnownObject.Shelves)),
            LongestCabinetRun = Find(KnownObject.Cabinet).Cluster(GroupReach).Select(group => group.Count).DefaultIfEmpty(0).Max(),
            MissilesInTriplets = CountTriplets(Find(KnownObject.Missile)),
            MirroredPads = CountMirroredPads(),
            CannonStripes = cannon is null ? null : Find(KnownObject.WallInscriptions).Count(point => Distance(point, cannon.Value) <= StripeReach),
            CannonOpenPixels = cannon is null ? null : MeasureOpenFloor(cannon.Value),
            CannonHitPoints = cannon is null ? default : CountHitPointsNear(cannon.Value),
            CannonWalk = _analysis.Elements.FirstOrDefault(element => element.Kind == ElementKind.MountedCannon)?.Distance,
            RoomProportion = MeasureRoomProportion(),
            Spread = MeasureSpread(),
            Graph = _model.Theme == LevelTheme.Lab ? RoomGraph.Build(_survey) : null,
            RepeatedRooms = CountRepeatedRooms(),
            FightGap = MeasureFightGap(),
            SpawnedPerThousand = spawners.Length == 0 ? null : 1000f * spawners.Sum(program => program.Monsters.Count()) / Math.Max(1, spawners.Sum(program => program.Monsters.Sum(GetHitPoints))),
            LargestSpawner = spawners.Length == 0 ? null : spawners.Max(program => program.Monsters.Count()),
            SitesPassedEarly = CountSitesPassedEarly(),
            SolidNearDoorways = CountNearDoorways(floorPieces.Where(piece => _objects.GetBlocking(piece.Id) == BlockingClass.Solid).Select(GetPoint).ToArray(), doorways, DoorwayReach),
            HittableNearDoorways = CountNearDoorways(floorPieces.Where(piece => _objects.GetBlocking(piece.Id) == BlockingClass.Hittable).Select(GetPoint).ToArray(), doorways, DoorwayReach),
            MonitorsNearDoorways = CountNearDoorways(monitors.Select(GetPoint).ToArray(), doorways, MonitorDoorwayReach),
            PiecesInsideOthers = CountInsideOthers(blocking),
            PickupsUnderPieces = CountUnder(loose, blocking),
            MonitorsOnTallFurniture = CountNear(monitors.Select(GetPoint).ToArray(), TallFurniture.SelectMany(Find).ToArray(), MonitorFurnitureReach),
            MonitorsOnSupports = new MeasureShare(monitors.Count(piece => piece.SupportIndex >= 0), monitors.Length),
            BoxesOnFurniture = new MeasureShare(boxes.Count(piece => piece.SupportIndex >= 0 && BoxSupports.Contains(_model.Objects[piece.SupportIndex.Value].Id)), boxes.Length),
            DeskTopsOnSupports = new MeasureShare(deskTops.Count(piece => piece.SupportIndex >= 0), deskTops.Length),
            AmmoBeforeFights = new MeasureShare(_survey.Items.Count(item => item is { Category: ItemCategory.Ammo, Before: not null }),
                _survey.Items.Count(item => item.Category == ItemCategory.Ammo)),
            MoneyOffRoute = new MeasureShare(loose.Count(item => item.Category == ItemCategory.Money && item.OffRoute > OffRouteWalk), loose.Count(item => item.Category == ItemCategory.Money)),
            HealthInCrates = CountInCrates(ItemCategory.Health),
            AmmoInCrates = CountInCrates(ItemCategory.Ammo),
            ArmorInCrates = CountInCrates(ItemCategory.Armor),
            PileSize = loose.Length == 0 ? null : (float)loose.Length / loose.Select(item => new PointF(item.Position.X, item.Position.Y)).ToArray().Cluster(PileReach).Count
        };
    }

    private PointF[] Find(KnownObject id) => _positions[id].ToArray();

    private PointF[] Find(Func<KnownObject, bool> matches) => _positions.Where(group => matches(group.Key)).SelectMany(group => group).ToArray();

    private PointF GetPoint(DecorObject piece) => _model.Lattice.ToWorld(piece.U, piece.V);

    private MapSegment[] GetDoorways() => _survey.GetDoorways().Where(doorway => doorway.Kind.IsPassage).Select(doorway => doorway.Span).ToArray();

    private static MeasureShare CountNearDoorways(PointF[] points, MapSegment[] doorways, float reach) =>
        new(points.Count(point => doorways.Any(doorway => doorway.MeasureDistance(point) <= reach)), points.Length);

    private CollisionBox[] GetBoxes(DecorObject[] pieces, float share) => pieces.Select(piece =>
    {
        _objects.TryFind(piece.Id, out var obj);
        var point = GetPoint(piece);
        return CollisionBox.Create(obj, point.X, point.Y, share, 0);
    }).ToArray();

    private MeasureShare CountInsideOthers(DecorObject[] pieces)
    {
        var boxes = GetBoxes(pieces, InsideShare);
        int inside = 0;
        for (int index = 0; index < boxes.Length; index++)
        {
            for (int otherIndex = 0; otherIndex < boxes.Length; otherIndex++)
            {
                if (otherIndex != index && boxes[index].Overlaps(boxes[otherIndex]))
                {
                    inside++;
                    break;
                }
            }
        }

        return new MeasureShare(inside, boxes.Length);
    }

    private MeasureShare CountUnder(LevelItem[] items, DecorObject[] pieces)
    {
        var boxes = GetBoxes(pieces, CollisionBox.ExtentShare);
        int under = items.Count(item => boxes.Any(box => Math.Abs(item.Position.X - box.X) < box.HalfWidth && Math.Abs(item.Position.Y - box.Y) < box.HalfHeight));
        return new MeasureShare(under, items.Length);
    }

    private MeasureShare CountInCrates(ItemCategory category) =>
        new(_survey.Items.Count(item => item.Category == category && item.InCrate), _survey.Items.Count(item => item.Category == category));

    private WallSample GetWallSample(KnownObject id, byte dir, float x, float y)
    {
        var cell = _model.Lattice.GetCell(x, y);
        var sides = _model.Edges.GetWalledSides(cell, _model.Cells.Contains, static kind => kind.IsClosed);

        bool open = sides == WallSides.None;
        for (int du = -2; du <= 2 && open; du++)
        {
            for (int dv = -2; dv <= 2 && open; dv++)
            {
                open = _model.Cells.Contains(new Cell(cell.U + du, cell.V + dv));
            }
        }

        return new WallSample(id, dir, sides, open);
    }

    private MeasureShare CountAwayFromWalls(IReadOnlyList<PointF> points)
    {
        var inside = points
            .Where(point => _model.Cells.Contains(_model.Lattice.GetCell(point.X, point.Y)))
            .ToArray();
        
        return new MeasureShare(inside.Count(point => GetWallSample(KnownObject.Computer, 0, point.X, point.Y).Sides == WallSides.None), inside.Length);
    }

    private static MeasureShare CountNear(IReadOnlyList<PointF> subjects, IReadOnlyList<PointF> partners, float reach) =>
        new(subjects.Count(subject => partners.Any(partner => Distance(subject, partner) <= reach)), subjects.Count);

    private MeasureShare CountInPlaces(Func<LevelPlace, bool> predicate)
    {
        var decor = OfficeDecor.SelectMany(Find).ToArray();
        int count = 0;
        foreach (var point in decor)
        {
            int place = _survey.FindPlace(point.X, point.Y);
            if (place != 0 && predicate(_survey.Places[place - 1]))
            {
                count++;
            }
        }

        return new MeasureShare(count, decor.Length);
    }

    private bool IsStorage(LevelPlace place) => _storagePlaces.Value.Contains(place.Id);

    private HashSet<int> FindStoragePlaces()
    {
        var storage = new Dictionary<int, int>();
        var furniture = new Dictionary<int, int>();
        foreach (var sprite in _model.Sprites.Where(sprite => Storage.Contains(sprite.Id) || Furniture.Contains(sprite.Id)))
        {
            int at = _survey.FindPlace(sprite.Position.X, sprite.Position.Y);
            var counts = Storage.Contains(sprite.Id) ? storage : furniture;
            counts[at] = counts.GetValueOrDefault(at) + 1;
        }

        return storage.Where(pair => pair.Value >= StorageDominance * (pair.Value + furniture.GetValueOrDefault(pair.Key))).Select(pair => pair.Key).ToHashSet();
    }

    private MeasureShare CountItems(IReadOnlyList<LevelItem> items, ItemCategory category, Func<WallSample, bool> predicate)
    {
        var chosen = items.Where(item => item.Category == category && _model.Cells.Contains(_model.Lattice.GetCell(item.Position.X, item.Position.Y))).ToArray();
        return new MeasureShare(chosen.Count(item => predicate(GetWallSample(item.Id, 0, item.Position.X, item.Position.Y))), chosen.Length);
    }

    private static MeasureShare CountPiles(IReadOnlyList<LevelItem> items)
    {
        var piles = items.Select(item => new PointF(item.Position.X, item.Position.Y)).ToArray().Cluster(PileReach);
        return new MeasureShare(piles.Count(pile => pile.Count >= 2), piles.Count);
    }

    private static MeasureShare CountGrouped(IReadOnlyList<PointF> points) =>
        new(points.Count(point => points.Any(other => other != point && Distance(point, other) <= GroupReach)), points.Count);

    private static MeasureShare CountTriplets(IReadOnlyList<PointF> points)
    {
        var groups = points.Cluster(MissileReach);
        return new MeasureShare(groups.Where(group => group.Count == 3).Sum(group => group.Count), points.Count);
    }

    private MeasureShare CountMirroredPads()
    {
        var groups = Pads.SelectMany(Find).ToArray().Cluster(PadReach).Where(group => group.Count >= 2).ToArray();
        int mirrored = 0;
        foreach (var group in groups)
        {
            float x = group.Average(point => point.X), y = group.Average(point => point.Y);
            if (group.All(pad => group.Any(other => MathF.Abs(other.X - x + pad.X - x) + MathF.Abs(other.Y - y + pad.Y - y) <= MirrorTolerance)))
            {
                mirrored++;
            }
        }

        return new MeasureShare(mirrored, groups.Length);
    }

    private int MeasureOpenFloor(PointF cannon)
    {
        float? nearest = null;
        foreach (var sprite in _model.Sprites)
        {
            if (sprite.Id != KnownObject.MountedCannon && sprite.Position.Z < FloorHeight && _objects.TryFind(sprite.Id, out var found) && found.IsSolid)
            {
                nearest = MathF.Min(nearest ?? float.MaxValue, Distance(cannon, new PointF(sprite.Position.X, sprite.Position.Y)));
            }
        }

        return nearest is null ? 0 : (int)MathF.Round(nearest.Value);
    }

    private MeasureShare CountHitPointsNear(PointF cannon)
    {
        var fights = _survey.Encounters.Where(encounter => encounter.Kind is EncounterKind.Ambush or EncounterKind.PlacedGroup).ToArray();
        int near = fights.Where(encounter => Distance(new PointF(encounter.Position.X, encounter.Position.Y), cannon) <= FightReach).Sum(encounter => encounter.HitPoints);
        return new MeasureShare(near, fights.Sum(encounter => encounter.HitPoints));
    }

    private float MeasureSpread()
    {
        var cells = _model.Cells.ToArray();
        float middleU = (float)cells.Average(c => c.U);
        float middleV = (float)cells.Average(c => c.V);
        float mean = cells.Average(c => MathF.Sqrt((c.U - middleU) * (c.U - middleU) + (c.V - middleV) * (c.V - middleV)));
        return MathF.Round(mean / (2f / 3 * MathF.Sqrt(cells.Length / MathF.PI)), 2);
    }

    private float? MeasureRoomProportion()
    {
        float[] ratios = _survey.Places
            .Where(place => place.Kind == PlaceKind.Room)
            .Where(place => place.WidthPixels > 0)
            .Select(place => place.LengthPixels / (float)place.WidthPixels)
            .Order()
            .ToArray();
        return ratios.Length == 0 ? null : MathF.Round(ratios[ratios.Length / 2], 2);
    }

    private int CountRepeatedRooms()
    {
        var byPlace = _model.Sprites
            .Where(sprite => !Unrepeated.Contains(sprite.Id) && (Storage.Contains(sprite.Id) || Furniture.Contains(sprite.Id)))
            .GroupBy(sprite => _survey.FindPlace(sprite.Position.X, sprite.Position.Y))
            .Where(group => group.Key != 0 && group.Count() >= RepeatMinObjects)
            .Select(group => group.ToArray())
            .ToArray();

        int repeated = 0;
        for (int first = 0; first < byPlace.Length; first++)
        {
            for (int second = first + 1; second < byPlace.Length; second++)
            {
                var (small, large) = byPlace[first].Length <= byPlace[second].Length ? (byPlace[first], byPlace[second]) : (byPlace[second], byPlace[first]);
                int matched = MatchLayouts(small, large);
                if (matched >= RepeatMinMatches && matched * 2 >= small.Length)
                {
                    repeated++;
                }
            }
        }

        return repeated;
    }

    private static int MatchLayouts(MergedMapSprite[] small, MergedMapSprite[] large)
    {
        var byId = large.ToLookup(sprite => sprite.Id);
        var offsets = new Dictionary<(int X, int Y), int>();
        foreach (var sprite in small)
        {
            foreach (var other in byId[sprite.Id])
            {
                var key = ((int)MathF.Round(other.Position.X - sprite.Position.X), (int)MathF.Round(other.Position.Y - sprite.Position.Y));
                offsets[key] = offsets.GetValueOrDefault(key) + 1;
            }
        }

        int best = 0;
        foreach (((int dx, int dy), int _) in offsets.OrderByDescending(pair => pair.Value).Take(6))
        {
            int matched = small.Count(sprite => byId[sprite.Id].Any(other => other.Dir == sprite.Dir
                && MathF.Abs(other.Position.X - sprite.Position.X - dx) < RepeatTolerance && MathF.Abs(other.Position.Y - sprite.Position.Y - dy) < RepeatTolerance));
            best = Math.Max(best, matched);
        }

        return best;
    }

    private float? MeasureFightGap()
    {
        var fights = _survey.Encounters
            .Where(encounter => encounter.Kind is EncounterKind.Ambush or EncounterKind.PlacedGroup or EncounterKind.Boss && encounter.HitPoints > 0)
            .ToArray();
        
        int total = fights.Sum(encounter => encounter.HitPoints);
        int[] big = fights.Where(encounter => encounter.HitPoints >= BigFightShare * total).Select(encounter => encounter.Route).Order().ToArray();
        if (big.Length < 2)
        {
            return null;
        }

        float tour = Math.Max(1, _analysis.RoutePixels);
        float[] gaps = big.Zip(big.Skip(1), (before, after) => (after - before) / tour).Order().ToArray();
        return MathF.Round(gaps[gaps.Length / 2], 3);
    }

    private MeasureShare CountSitesPassedEarly()
    {
        var lattice = _analysis.Grid.Lattice;
        var points = _analysis.Tour.Select(lattice.GetCenter).ToArray();
        float[] walked = new float[points.Length];
        for (int index = 1; index < points.Length; index++)
        {
            walked[index] = walked[index - 1] + Distance(points[index - 1], points[index]);
        }

        float tour = Math.Max(1, walked.Length == 0 ? 0 : walked[^1]);
        var sites = _analysis.Elements.Where(element => element.Kind == ElementKind.DynamiteSite).ToArray();
        int early = 0;
        foreach (var site in sites)
        {
            int seen = Array.FindIndex(points, point => MathF.Abs(point.X - site.X) < ScreenHalfWidth && MathF.Abs(point.Y - site.Y) < ScreenHalfHeight);
            if (seen >= 0 && site.Progress - walked[seen] / tour > EarlyShare)
            {
                early++;
            }
        }

        return new MeasureShare(early, sites.Length);
    }

    private static float Distance(PointF first, PointF second) => MathF.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    private int GetHitPoints(KnownObject id) => _objects.TryFind(id, out var obj) ? obj.MaxHp : 0;
}

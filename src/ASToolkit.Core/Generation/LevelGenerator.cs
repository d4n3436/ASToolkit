using System.Drawing;
using System.Collections.Concurrent;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Content;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Outdoor;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the generation of campaign levels 2 to 10.
/// </summary>
public sealed class LevelGenerator
{
    /// <summary>
    /// Represents the number of levels in an add-on.
    /// </summary>
    public const int AddOnLevelCount = 5;

    private const float BreakerSceneReach = 130;
    private const int MaxLayouts = 8;

    private const int SeedPrime = 7919;
    private const int BudgetSalt = 17;
    private const int ContentSalt = 101;
    private const int RoofSalt = 211;
    private const int MaxBreakerRetries = 20;
    private const float GeneratorMargin = 0.04f;
    private const float FloorMiss = 0.15f;
    private static readonly (float Min, float Max) GeneratorBand = (0.08f, 0.3f);
    private const float BlackoutSearchPixels = 5200;
    private const int MaxPlanAttempts = 200;
    private const int OwnProfileAttempts = 50;
    private const float GateClearance = 100;
    private const float SiteClearance = 150;
    private const float LaneClearance = 100;
    private const float LaneStep = 40;

    // Campaign levels the add-on levels take their structure from
    private static readonly int[][] AddOnSlots = [[1, 4, 5, 3, 9], [1, 5, 5, 8, 5]];

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private static readonly string[] LabLevels = ["Level_02.map", "Level_03.map", "Level_04.map", "Level_05.map"];
    private static readonly string[] LabSiteLevels = ["Level_03.map", "Level_04.map"];
    private static readonly string[] MilitarySiteLevels = ["Level_07.map", "Level_08.map", "Level_09.map"];
    private static readonly string[] AddOnLabSiteLevels = ["ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map"];
    private static readonly string[] AddOnMilitarySiteLevels = ["ADDON/Level_04.map", "ADDON/Level_05.map", "ADDON2/Level_04.map"];

    private readonly bool _addOnStyle;
    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ReferenceLevelSet _references;
    private readonly ArmyTable _armies;
    private readonly Lazy<OriginalContentSet> _content;
    private readonly Lazy<DressingLibrary> _labLibrary;
    private readonly Lazy<DressingLibrary> _militaryLibrary;
    private readonly Lazy<RoomFurnisher> _militaryFurnisher;
    private readonly Lazy<RoomFurnisher> _labFurnisher;
    private readonly Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>> _breakerScenes;
    private readonly Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>> _cannonScenes;
    private readonly Lazy<(Dictionary<byte, List<IReadOnlyList<PropPlacement>>> Marks, float Share)> _missileMarks;
    private readonly OutdoorLevelBuilder _outdoor;
    private readonly Lazy<SiteTemplate[]> _labSites;
    private readonly Lazy<SiteTemplate[]> _militarySites;
    private readonly Lazy<KeypadTable> _keypads;
    private readonly Lazy<LevelProfile[]> _labProfiles;
    private readonly Lazy<LevelProfile[]> _militaryProfiles;
    private readonly ConcurrentDictionary<int, Lazy<OriginalLevel>> _originals = new();
    private readonly ConcurrentDictionary<(string Name, ThemeSettings Settings), Lazy<LevelProfile>> _profiles = new();
    private readonly ConcurrentDictionary<string, Lazy<EncounterProfile>> _encounters = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelGenerator"/> class.
    /// </summary>
    public LevelGenerator(string gameFolder, ObjectTable objects, ReferenceLevelSet references, bool addOnStyle = false)
    {
        _addOnStyle = addOnStyle;
        _gameFolder = gameFolder;
        _objects = objects;
        _references = references;
        _armies = ArmyTable.Load(references, ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName).Concat(AddOnLevels));
        string[] sources = ReferenceLevelSet.CampaignLevels
            .Select(ReferenceLevelSet.GetCampaignName)
            .Concat(addOnStyle ? AddOnLevels : [])
            .ToArray();
        
        _content = new Lazy<OriginalContentSet>(() => OriginalContentSet.Load(references, sources));
        _labFurnisher = new Lazy<RoomFurnisher>(() => CreateFurnisher(references, objects, LevelTheme.Lab, sources));
        _militaryFurnisher = new Lazy<RoomFurnisher>(() => CreateFurnisher(references, objects, LevelTheme.Military, sources));
        _labLibrary = new Lazy<DressingLibrary>(() => DressingLibrary.Build(references, objects, LevelTheme.Lab, sources, GetWallDressing(references, LevelTheme.Lab), references.GetWallClearance(LevelTheme.Lab), references.GetFacings(LevelTheme.Lab)));
        _militaryLibrary = new Lazy<DressingLibrary>(() => DressingLibrary.Build(references, objects, LevelTheme.Military, sources, GetWallDressing(references, LevelTheme.Military), references.GetWallClearance(LevelTheme.Military), references.GetFacings(LevelTheme.Military)));
        _keypads = new Lazy<KeypadTable>(() => KeypadTable.Load(references, sources));
        _labProfiles = new Lazy<LevelProfile[]>(() => LabLevels.Select(name => GetProfile(references.GetSurvey(name), ThemeSettings.Lab)).ToArray());
        _militaryProfiles = new Lazy<LevelProfile[]>(() => references.LevelNames.Order().Select(references.GetSurvey).Where(survey => survey.Theme == LevelTheme.Military)
            .Select(survey => GetProfile(survey, ThemeSettings.Military)).ToArray());
        _breakerScenes = new Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>>(() => OriginalContent.LoadBreakerScenes(references, ["Level_02.map", "Level_03.map", "Level_04.map"]));
        _outdoor = new OutdoorLevelBuilder(_armies);
        _cannonScenes = new Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>>(() => OriginalContent.LoadCannonScenes(references, ["Level_08.map"]));
        _missileMarks = new Lazy<(Dictionary<byte, List<IReadOnlyList<PropPlacement>>>, float)>(() => OriginalContent.LoadMissileMarks(references, ["Level_06.map", "Level_07.map", "Level_08.map", "Level_09.map", "Level_10.map"]));
        _labSites = new Lazy<SiteTemplate[]>(() => OriginalContent.LoadDynamiteSites(references, addOnStyle ? LabSiteLevels.Concat(AddOnLabSiteLevels).ToArray() : LabSiteLevels, LevelTheme.Lab));
        _militarySites = new Lazy<SiteTemplate[]>(() => OriginalContent.LoadDynamiteSites(references, addOnStyle ? MilitarySiteLevels.Concat(AddOnMilitarySiteLevels).ToArray() : MilitarySiteLevels, LevelTheme.Military));
    }

    /// <summary>
    /// Returns the original map a generated level takes its other chunks from.
    /// </summary>
    public string GetTemplatePath(int level) => Path.Combine(_gameFolder, "Maps", ReferenceLevelSet.GetCampaignName(level));

    /// <summary>
    /// Generates a level in one pass, drawing its layout again when the tour cannot reach every objective.
    /// </summary>
    public GeneratedLevel Generate(int seed, int level) => Generate(seed, level, _references.GetTarget(level));

    /// <summary>
    /// Generates an add-on level, numbered 1 to 5. The seed's parity picks the add-on it follows. Level 1 is outdoor like that
    /// add-on's level 1, and the others take their structure from the campaign level closest to that add-on's level, and their
    /// monsters, pickups and design targets from that level, with the hit points of both add-ons' level on average. It needs
    /// the generator to have been created in add-on style.
    /// </summary>
    public GeneratedLevel GenerateAddOn(int seed, int number)
    {
        if (!_addOnStyle)
        {
            throw new InvalidOperationException("The generator was not created in add-on style.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, AddOnLevelCount);
        string[] names = [$"ADDON/Level_{number:00}.map", $"ADDON2/Level_{number:00}.map"];
        int hitPoints = (int)MathF.Round(MathF.Sqrt(names.Aggregate(1.0f, (product, n) => product * Math.Max(1, _content.Value.GetLevel(n).HitPoints))));
        int addOn = Math.Abs(seed) % 2;
        int slot = AddOnSlots[addOn][number - 1];
        var target = _references.GetTarget(slot, hitPoints, names[addOn]);
        var level = Generate(seed, slot, target);
        level.AddOnNumber = number;
        level.Template = slot == 1 ? names[addOn] : $"ADDON/Level_{number:00}.map";
        return level;
    }

    /// <summary>
    /// Returns the original map a generated level takes its other chunks from.
    /// </summary>
    public string GetTemplatePath(GeneratedLevel level) => Path.Combine(_gameFolder, "Maps", level.Template);

    // Build the level on the first layout whose objectives the tour reaches without secret walls or forced gates
    private GeneratedLevel Generate(int seed, int level, LevelTarget target)
    {
        GeneratedLevel best = null!;
        for (int layoutIndex = 0; layoutIndex < MaxLayouts; layoutIndex++)
        {
            best = BuildAnalyzed(GetLayoutSeed(seed, level, layoutIndex), level, target);
            if (best.Theme == LevelTheme.Outdoor || best.Analysis?.FallbackUsage is { UsedSecretWalls: false, ForcedGates: false, Unreached: 0 })
            {
                break;
            }
        }

        return best;
    }

    // Hang the wall dressing and lay the gore of copied windows, furnish every room from its type's recipe away from the breaker and the
    // fence gates, stand the breaker's scene, mark the missiles and keep the floor joined
    private (PlacedDecor[] Decor, Dictionary<Cell, int> Blocked) Dress(Random rng, LevelTheme theme, LayoutPlan plan, FineLayout layout, MapBuilder map,
        LevelOccupancy occupancy, IReadOnlySet<KnownObject> held, Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf, HashSet<Cell> floor, HashSet<Cell> keepClear, BreakerSpot? breaker, IReadOnlyDictionary<EdgeKey, EdgeKind> placedEdges, LevelTarget target,
        MapResourceBuilder originalMap, LevelModel original)
    {
        bool isLab = theme == LevelTheme.Lab;
        var (wallLattice, walls) = isLab ? (layout.Lattice, placedEdges) : LevelExtractor.ReadMilitaryEdges(map.Sprites) ?? (layout.Lattice, placedEdges);
        var decor = new List<PlacedDecor>();
        AddBreakerScene(decor, layout, occupancy, breaker);
        var clear = new HashSet<Cell>(keepClear);
        if (breaker is not null && plan.KeyRoom is not null)
        {
            clear.UnionWith(layout.GetRegionCells(plan.KeyRoom.Id).Where(cell => IsNearBreaker(layout.Lattice.GetCenter(cell), breaker.Position)));
        }

        var gates = map.Sprites.Where(sprite => sprite.Id == KnownObject.FenceEmitter).Select(sprite => layout.Lattice.ToLattice(sprite.Position.X, sprite.Position.Y)).ToArray();
        clear.UnionWith(floor.Where(cell => gates.Any(gate => IsWithin(layout.Lattice, cell, (gate.U, gate.V, GateClearance)))));
        occupancy.Reserve(clear);
        decor.AddRange(Dresser.Dress(rng, isLab ? _labLibrary.Value : _militaryLibrary.Value, floor, layout.Edges, keepClear, layout.Lattice, occupancy, held));
        (float corpseRate, float bloodRate) = MeasureGore(originalMap, original);
        RoomDecorator.Decorate(theme, plan, layout, decor, floor, keepClear, occupancy, target.ThemeDecor, target.Facings, corpseRate, bloodRate);
        var furnisher = isLab ? _labFurnisher.Value : _militaryFurnisher.Value;
        var (_, furniture) = furnisher.Furnish(rng, plan, layout, _references.GetAtlas(target.OriginalSurvey.Name), clear, occupancy, companionsOf, wallLattice, walls, target.Facings, target.WallClearance);
        decor.AddRange(furniture);
        RoomDecorator.MarkMissiles(rng, decor, _missileMarks.Value.Marks, _missileMarks.Value.Share, floor, keepClear, layout.Lattice);
        var placed = decor.ToArray();
        var blocked = new Dictionary<Cell, int>();
        foreach (var piece in placed)
        {
            (float x, float y) = layout.Lattice.ToWorld(piece.U, piece.V);
            foreach (var cell in Dresser.GetFootprint(layout.Lattice, _objects, piece.Id, x, y, 0))
            {
                blocked[cell] = blocked.GetValueOrDefault(cell) + 1;
            }
        }

        return (placed, blocked);
    }

    private LevelOccupancy CreateOccupancy(LevelTheme theme, LayoutPlan plan, FineLayout layout, IReadOnlySet<Cell> floor, Cell start)
    {
        var lattice = layout.Lattice;
        var doorways = plan.Links
            .Select((link, index) => (link.Kind, Edges: layout.LinkEdges[index]))
            .Where(link => link.Kind is LinkKind.Door or LinkKind.Arch or LinkKind.Fence)
            .SelectMany(link => link.Edges)
            .Select(edge => edge.Axis == 'v'
                ? new MapSegment(lattice.ToWorld(edge.A, edge.B), lattice.ToWorld(edge.A + 1, edge.B))
                : new MapSegment(lattice.ToWorld(edge.A, edge.B), lattice.ToWorld(edge.A, edge.B + 1)));
        
        var clearances = new Dictionary<BlockingClass, float>();
        foreach (var blocking in new[] { BlockingClass.Solid, BlockingClass.Hittable })
        {
            float? clearance = _references.Relations.GetDoorClearance(theme, blocking);
            if (clearance is not null)
            {
                clearances[blocking] = clearance.Value;
            }
        }

        return new LevelOccupancy(_objects, lattice, floor, layout.Edges, start, doorways, clearances,
            (id, other, raised) => _references.Relations.GetSpacing(theme, id, other, raised));
    }

    private static RoomFurnisher CreateFurnisher(ReferenceLevelSet references, ObjectTable objects, LevelTheme setting, IReadOnlyList<string> sources) =>
        new(RoomRecipe.Measure(sources.Where(name => references.GetSurvey(name).Setting == setting).SelectMany(name => references.GetAtlas(name).Rooms),
                references.GetSurvey(references.LevelNames.First()).Lattice),
            objects, GetDressingDecor(references, setting), references.GetSurvey(references.LevelNames.First()).Lattice);

    private static (float Corpses, float Blood) MeasureGore(MapResourceBuilder originalMap, LevelModel original)
    {
        float cells = Math.Max(1, original.Cells.Count);
        return (originalMap.Sprites.Count(sprite => sprite.Id.IsCorpse) / cells,
            originalMap.Sprites.Count(sprite => sprite.Id.IsBlood) / cells);
    }

    // Clear the breaker's surroundings in its room and stand the breaker's scene there
    private static void AddBreakerScene(List<PlacedDecor> decor, FineLayout layout, LevelOccupancy occupancy, BreakerSpot? breaker)
    {
        if (breaker is null)
        {
            return;
        }

        for (int propIndex = 0; propIndex < breaker.Props.Count; propIndex++)
        {
            var prop = breaker.Props[propIndex];
            float x = breaker.Position.X + prop.Dx;
            float y = breaker.Position.Y + prop.Dy;
            if (!occupancy.Fits(prop.Id, x, y, prop.Z))
            {
                continue;
            }

            (float u, float v) = layout.Lattice.ToLattice(x, y);
            decor.Add(new PlacedDecor(prop.Id, u, v, prop.Z, prop.Dir, new byte[8], (-1, propIndex), null, SpriteSource.CopiedScene));
            occupancy.Add(prop.Id, x, y, prop.Z);
        }
    }

    private LevelProfile GetProfile(LevelSurvey survey, ThemeSettings settings) =>
        _profiles.GetOrAdd((survey.Name, settings), key => new Lazy<LevelProfile>(() => LevelProfile.Measure(survey, key.Settings))).Value;

    private EncounterProfile GetEncounters(string name) =>
        _encounters.GetOrAdd(name, key => new Lazy<EncounterProfile>(() => EncounterProfile.Measure(_references.GetSurvey(key), _references.GetLevel(key).Analysis, _objects))).Value;

    private OriginalLevel GetOriginal(int level) => _originals.GetOrAdd(level, l => new Lazy<OriginalLevel>(() => LoadOriginal(l))).Value;

    private OriginalLevel LoadOriginal(int level)
    {
        string name = ReferenceLevelSet.GetCampaignName(level);
        var reference = _references.GetLevel(name);
        return new OriginalLevel(reference.Map, reference.Model, File.ReadAllBytes(GetTemplatePath(level)), LevelScriptWriter.ReadMusicLines(Path.Combine(_gameFolder, "Maps", Path.ChangeExtension(name, ".lgc"))));
    }

    // Analyze the level's route through its map and script bytes
    private GeneratedLevel Analyze(GeneratedLevel level)
    {
        var map = MapResourceBuilder.Parse(level.Map.ToBytes(GetOriginal(level.Level).Template));
        var script = LevelScriptReader.Parse(LevelScriptWriter.Format(level.Script));
        level.Analysis = RouteAnalyzer.Analyze(ReferenceLevelSet.GetCampaignName(level.Level), map, script, _objects);
        return level;
    }

    private static int GetLayoutSeed(int seed, int level, int layoutIndex) => seed * 100_000 + level * 100 + layoutIndex;

    // Draw the layout and structure from the layout seed, and the budget, the decor and content and the roof from streams of their own
    private GeneratedLevel Build(int layoutSeed, int level, LevelTarget target, int? generatorRoom = null) =>
        Build(new Random(layoutSeed), new Random(unchecked(layoutSeed * SeedPrime + BudgetSalt)), new Random(unchecked(layoutSeed * SeedPrime + ContentSalt)),
            new Random(unchecked(layoutSeed * SeedPrime + RoofSalt)), level, target, generatorRoom);

    // Build and analyze a level, and build it again with its generator moved when the tour reaches the first one outside the originals' band,
    // keeping the build whose first generator falls nearer the band
    private GeneratedLevel BuildAnalyzed(int layoutSeed, int level, LevelTarget target)
    {
        var built = Analyze(Build(layoutSeed, level, target));
        int? generatorRoom = FindGeneratorRoom(built, target);
        if (generatorRoom is null)
        {
            return built;
        }

        var moved = Analyze(Build(layoutSeed, level, target, generatorRoom));
        return MeasureBandMiss(moved) <= MeasureBandMiss(built) ? moved : built;
    }

    private static float MeasureBandMiss(GeneratedLevel level)
    {
        var first = level.Analysis?.Objectives.FirstOrDefault(objective => objective.Kind == ObjectiveKind.Generator);
        return first is null ? float.MaxValue : Math.Max(0, Math.Max(GeneratorBand.Min - first.Progress, first.Progress - GeneratorBand.Max));
    }

    // Pick the generator room whose walk from the start, scaled by how the tour walked to the first generator, comes nearest the original's
    // share of the tour
    private static int? FindGeneratorRoom(GeneratedLevel level, LevelTarget target)
    {
        var plan = level.Plan;
        var analysis = level.Analysis;
        if (plan is null || analysis is null || plan.Gate != GateKind.Fences || plan.KeyRoom is null || plan.GeneratorRooms.Count <= 1)
        {
            return null;
        }

        var key = plan.KeyRoom;
        var first = analysis.Objectives.FirstOrDefault(objective => objective.Kind == ObjectiveKind.Generator);
        if (first is null || first.Progress >= GeneratorBand.Min && first.Progress <= GeneratorBand.Max)
        {
            return null;
        }

        float walk = plan.GeneratorRooms.First(candidate => candidate.Room == key).WalkPixels;
        float routePerWalk = first.Progress * analysis.RoutePixels / Math.Max(1, walk);
        float share = Math.Clamp(target.OriginalSurvey.Landmarks.Where(l => l.Kind == LandmarkKind.Generator).Select(l => l.Route / (float)Math.Max(1, target.OriginalSurvey.Signature.TourPixels))
            .DefaultIfEmpty(0).Min(), GeneratorBand.Min + GeneratorMargin, GeneratorBand.Max - GeneratorMargin);
        var best = plan.GeneratorRooms.MinBy(candidate => Math.Abs(candidate.WalkPixels * routePerWalk - share * analysis.RoutePixels));
        return best.Room == key ? null : best.Room.Id;
    }

    private GeneratedLevel Build(Random layoutRng, Random budgetRng, Random contentRng, Random roofRng, int level, LevelTarget target, int? generatorRoom = null, int retries = 0)
    {
        if (level == 1)
        {
            return BuildOutdoor(layoutRng, target);
        }

        var (originalMap, original, _, musicLines) = GetOriginal(level);
        var theme = original.Theme;
        var settings = theme == LevelTheme.Lab ? ThemeSettings.Lab : ThemeSettings.Military;
        int originalUnits = theme == LevelTheme.Lab ? original.Cells.Count : original.Cells.Count / 4;
        var gate = ChooseGate(layoutRng, target, theme);
        var budget = BudgetPlanner.Plan(budgetRng, level, target, _content.Value, _objects);
        int spawnClosets = theme == LevelTheme.Lab ? Math.Min(6, budget.Ambushes.Count / 3) : 0;
        int shortcuts = theme == LevelTheme.Lab ? layoutRng.Next(3) : 0;
        LayoutPlan? plan = null;
        var profile = GetProfile(_references.GetSurvey(ReferenceLevelSet.GetCampaignName(level)), settings);
        var own = target.ContentLevel != ReferenceLevelSet.GetCampaignName(level) && target.OriginalSurvey.Theme == theme ? GetProfile(target.OriginalSurvey, settings) : null;
        if (theme == LevelTheme.Lab && own is { Halls.Count: 0 } && gate is not { Kind: GateKind.Blackout, IsWing: false })
        {
            own = null;
        }

        var pool = theme == LevelTheme.Lab ? _labProfiles.Value : _militaryProfiles.Value;
        var request = new LayoutRequest(gate, theme == LevelTheme.Lab ? target.SecretClosets : 0, spawnClosets, shortcuts, theme == LevelTheme.Military ? target.OriginalPens : 0);
        // Draw again once with the floor scaled when the built rooms miss the original's room floor
        float floorScale = 1;
        bool rescaled = false;
        for (int attempt = 0; plan is null; attempt++)
        {
            if (attempt == MaxPlanAttempts)
            {
                throw new InvalidOperationException($"No layout for level {level}.");
            }

            int targetUnits = (int)(originalUnits * (0.9f + 0.15f * layoutRng.NextSingle()) * floorScale);
            var source = own is not null && attempt < OwnProfileAttempts ? own : profile;
            var drawn = source == own ? own.Sample(layoutRng, [own], targetUnits, gate.IsWing ? gate.Share : 0) : profile.Sample(layoutRng, pool, targetUnits, gate.IsWing ? gate.Share : 0);
            plan = LayoutBuilder.Build(layoutRng, drawn, request);
            float built = plan?.Regions.Where(region => region.Kind is RegionKind.Station or RegionKind.SideRoom or RegionKind.Wing or RegionKind.Pen).Sum(region => region.Units.Count) ?? 0;
            if (plan is not null && !rescaled && MathF.Abs(built / source.Units - 1) > FloorMiss)
            {
                floorScale = source.Units / built;
                rescaled = true;
                plan = null;
            }
        }

        if (generatorRoom is not null)
        {
            plan.KeyRoom = plan.Regions[generatorRoom.Value];
        }

        var layout = FineLayout.Build(plan);
        StructureBuilder.PlaceLattice(layout, theme);
        StructureBuilder.AddFakeDoors(layoutRng, layout, plan);
        var map = new MapBuilder(_armies);
        var closetCells = layout.Cells
            .Where(p => plan.Regions[p.Value].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset)
            .Select(p => p.Key)
            .ToHashSet();
        
        var shown = layout.Cells.Keys
            .Where(c => !closetCells.Contains(c))
            .ToHashSet();
        
        var visibleEdges = layout.Edges
            .Where(p => shown.Contains(p.Key.Cells.First) || shown.Contains(p.Key.Cells.Second))
            .OrderBy(p => p.Key.Axis)
            .ThenBy(p => p.Key.A)
            .ThenBy(p => p.Key.B)
            .ToDictionary();
        
        var closetEdges = layout.Edges
            .Where(p => !visibleEdges.ContainsKey(p.Key) && (closetCells.Contains(p.Key.Cells.First) || closetCells.Contains(p.Key.Cells.Second)))
            .OrderBy(p => p.Key.Axis)
            .ThenBy(p => p.Key.A)
            .ThenBy(p => p.Key.B)
            .ToDictionary();
        
        var spawnWalls = theme == LevelTheme.Lab ? StructureBuilder.BuildLab(layoutRng, map, layout, visibleEdges, contentRng, closetEdges) : [];
        var placedEdges = theme == LevelTheme.Lab ? LevelExtractor.ReadLabEdges(layout.Lattice, map.Sprites, outdoor: false) : layout.Edges;
        ContentPlacer.PlaceBrokenKeypads(contentRng, map, _keypads.Value, layout.Lattice, target.Facings, placedEdges);
        (var doorMarks, float doorMarkShare) = OriginalContent.LoadDoorMarks(originalMap);
        ContentPlacer.PlaceDoorMarks(contentRng, map, layout.Lattice, placedEdges, doorMarks, doorMarkShare);
        
        if (theme == LevelTheme.Military)
        {
            StructureBuilder.BuildMilitary(layoutRng, map, layout, visibleEdges);
        }

        if (plan is { Gate: GateKind.Fences, GateLink: not null })
        {
            foreach (var fence in plan.Links.Where(link => link.Kind == LinkKind.Fence))
            {
                StructureBuilder.BuildFence(map, layout.Lattice, fence.Edges, settings.UnitCells);
            }
        }

        StructureBuilder.BuildRoof(roofRng, theme, layout, shown, map);
        map.Source = SpriteSource.Model;

        var startRoom = layout.GetRegionCells(plan.Stations[0].Id).ToArray();
        var start = startRoom[startRoom.Length / 2];
        var breaker = plan.Gate is GateKind.LockedDoors or GateKind.Blackout
            ? ContentPlacer.ChooseBreakerSpot(layoutRng, plan, layout, placedEdges, _breakerScenes.Value, _objects, target.WallClearance, target.Facings)
            : null;
        
        if (breaker is null && plan.Gate is GateKind.LockedDoors or GateKind.Blackout && retries < MaxBreakerRetries)
        {
            return Build(layoutRng, budgetRng, contentRng, roofRng, level, target, generatorRoom, retries + 1);
        }

        var floor = layout.Cells.Keys.Where(c => !closetCells.Contains(c)).ToHashSet();
        var keepClear = GetKeepClear(layout, start, settings.UnitCells);
        var occupancy = CreateOccupancy(theme, plan, layout, floor, start);
        occupancy.Reserve(keepClear);
        var held = originalMap.Sprites.Select(sprite => sprite.Id).ToHashSet();
        Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf = anchor => _references.Relations.GetCompanions(theme, anchor).Where(companion => held.Contains(companion.Id)).ToArray();

        var encounters = GetEncounters(target.ContentLevel);

        // Lay out the cannon and the sites before the decor
        PlacedSites? placedSites = null;
        if (theme == LevelTheme.Military)
        {
            var early = new PlacementContext(contentRng, level, plan, layout, map, _objects, start, new HashSet<Cell>(), keepClear, occupancy, companionsOf);
            placedSites = ContentPlacer.PlaceMilitarySites(early, budget, _militarySites.Value, _cannonScenes.Value, encounters);
            var siteSpots = GetSiteSpots(map, layout.Lattice, placedSites);
            var siteCells = layout.Cells.Keys.Where(c => siteSpots.Any(spot => IsWithin(layout.Lattice, c, spot))).ToArray();
            keepClear.UnionWith(siteCells);
            occupancy.Reserve(siteCells);
        }

        var (decor, blocked) = Dress(contentRng, theme, plan, layout, map, occupancy, held, companionsOf, floor, keepClear, breaker, placedEdges, target, originalMap, original);
        foreach (var piece in decor)
        {
            (float x, float y) = layout.Lattice.ToWorld(piece.U, piece.V);
            map.Add(piece.Id, x, y, piece.Z, piece.Dir, piece.Payload, unique: false, source: piece.Origin);
        }

        var context = new PlacementContext(contentRng, level, plan, layout, map, _objects, start, blocked.Keys.ToHashSet(), keepClear, occupancy, companionsOf);
        var player = startRoom
            .Where(c => !blocked.ContainsKey(c) && !layout.IsWalled(c))
            .OrderBy(c => Math.Abs(c.U - start.U) + Math.Abs(c.V - start.V))
            .FirstOrDefault(c => !context.IsInWall(layout.Lattice.GetCenter(c).X, layout.Lattice.GetCenter(c).Y, 20), start);
        
        (float px, float py) = layout.Lattice.GetCenter(player);
        
        map.Add(KnownObject.PlayerStart, px, py, 0, 96);
        context.Taken.Add(player);

        var script = new LevelScript
        {
            MusicLines = musicLines
        };
        
        ContentPlacer.Place(context, budget, target.HasCheckpoint, target.Style == LevelStyle.Blackout, script, breaker, theme == LevelTheme.Lab ? _labSites.Value : _militarySites.Value,
            visibleEdges, placedEdges, spawnWalls, _keypads.Value, target.Facings, _cannonScenes.Value, encounters, placedSites);
        
        return new GeneratedLevel
        {
            Level = level,
            Map = map.ToMap(new Vector2(Math.Max(0, px - 400), Math.Max(0, py - 300))),
            Script = script,
            Plan = plan,
            Layout = layout,
            Budget = budget,
            Template = ReferenceLevelSet.GetCampaignName(level),
            Lattice = layout.Lattice,
            Theme = theme,
            Provenance = map.GetProvenance()
        };
    }

    private GeneratedLevel BuildOutdoor(Random rng, LevelTarget target)
    {
        string name = target.ContentLevel;
        var style = name.StartsWith("ADDON2/", StringComparison.Ordinal) ? OutdoorStyle.City : name.StartsWith("ADDON/", StringComparison.Ordinal) ? OutdoorStyle.Compound : OutdoorStyle.Campaign;
        var original = _references.GetLevel(name);
        var music = LevelScriptWriter.ReadMusicLines(Path.Combine(_gameFolder, "Maps", Path.ChangeExtension(name, ".lgc")));
        var level = _outdoor.Build(rng, style, original.Map, original.Script.Exit, music);
        level.Template = name;
        return level;
    }

    // Keep broken keypads for jammed doors
    private static Func<KnownObject, float, bool> GetDressingDecor(ReferenceLevelSet references, LevelTheme theme) =>
        (id, z) => id != KnownObject.BrokenKeypad && references.GetThemeDecor(theme).Allows(id, z);

    // Copy only what hangs on walls or lies on the floor, the furniture being the furnisher's and the missile marks the decorator's
    private static Func<KnownObject, float, bool> GetWallDressing(ReferenceLevelSet references, LevelTheme theme) =>
        (id, z) => GetDressingDecor(references, theme)(id, z) && id.Family is null && !RoomDecorator.IsMissileMark(id, z);

    // Keep the floor clear around every site and pad, and along the line from the cannon to its kill zone
    private static List<(float U, float V, float Reach)> GetSiteSpots(MapBuilder map, Lattice lattice, PlacedSites placed)
    {
        var spots = map.Sprites
            .Where(sprite => sprite.Id is KnownObject.DynamiteSite or KnownObject.TeleportPad)
            .Select(sprite => lattice.ToLattice(sprite.Position.X, sprite.Position.Y))
            .Select(point => (point.U, point.V, SiteClearance))
            .ToList();
        int cannon = map.Sprites.FindIndex(sprite => sprite.Id == KnownObject.MountedCannon);
        if (placed.Cannon is not null && cannon >= 0)
        {
            var from = map.Sprites[cannon].Position;
            var to = placed.Cannon.Value.Far;
            int steps = (int)(MathF.Sqrt(MathF.Pow(to.X - from.X, 2) + MathF.Pow(to.Y - from.Y, 2)) / LaneStep);
            for (int step = 0; step <= steps; step++)
            {
                float t = step / (float)Math.Max(1, steps);
                (float u, float v) = lattice.ToLattice(from.X + t * (to.X - from.X), from.Y + t * (to.Y - from.Y));
                spots.Add((u, v, LaneClearance));
            }
        }

        return spots;
    }

    private static bool IsWithin(Lattice lattice, Cell cell, (float U, float V, float Reach) spot)
    {
        (float x, float y) = lattice.GetCenter(cell);
        (float sx, float sy) = lattice.ToWorld(spot.U, spot.V);
        return (x - sx) * (x - sx) + (y - sy) * (y - sy) <= spot.Reach * spot.Reach;
    }

    private static bool IsNearBreaker(PointF point, PointF breaker) =>
        MathF.Abs(point.X - breaker.X) < BreakerSceneReach && MathF.Abs(point.Y - breaker.Y) < BreakerSceneReach;

    // Keep clear the cells on both sides of every doorway, as close as the originals stand furniture to one, the cell in front of each fake
    // door and a square around the start
    private static HashSet<Cell> GetKeepClear(FineLayout layout, Cell start, int unitCells)
    {
        var keep = layout.LinkEdges.SelectMany(edges => edges).SelectMany(edge => new[] { edge.Cells.First, edge.Cells.Second }).ToHashSet();
        foreach ((var edge, bool isDouble) in layout.FakeDoors)
        {
            var doors = isDouble ? new[] { edge, StructureBuilder.GetPreviousEdge(edge) } : [edge];
            foreach (var door in doors)
            {
                keep.Add(door.Axis == 'v' ? door.Cells.Second : door.Cells.First);
            }
        }

        int radius = unitCells == 1 ? 1 : 2;
        for (int index = -radius; index <= radius; index++)
        {
            for (int innerIndex = -radius; innerIndex <= radius; innerIndex++)
            {
                keep.Add(new Cell(start.U + index, start.V + innerIndex));
            }
        }

        return keep;
    }

    private static GateRequest ChooseGate(Random rng, LevelTarget target, LevelTheme theme) => target.Style switch
    {
        LevelStyle.LockedDoors => new GateRequest(GateKind.LockedDoors, target.LockedShare ?? 0.5f, 0, WalkPixels: target.WalkToBreakerPixels ?? 0),
        LevelStyle.Blackout => new GateRequest(GateKind.Blackout, target.OriginalSurvey.Gates.FirstOrDefault(g => g.Kind == LevelStyle.Blackout)?.GatedShare ?? 0, BlackoutSearchPixels),
        LevelStyle.Fenced => new GateRequest(GateKind.Fences, target.FencedOffShare ?? 0.4f, 0,
            KeyProgress: target.OriginalSurvey.Landmarks.Where(l => l.Kind == LandmarkKind.Generator).Select(l => l.Route / (float)Math.Max(1, target.OriginalSurvey.Signature.TourPixels)).DefaultIfEmpty(0).Min()),
        _ => theme == LevelTheme.Military
            ? new GateRequest(GateKind.Fences, 0.2f + 0.1f * rng.NextSingle(), 0, IsWing: true)
            : ChooseLabWing(rng)
    };

    private static GateRequest ChooseLabWing(Random rng)
    {
        var kind = rng.Next(2) == 0 ? GateKind.LockedDoors : GateKind.Blackout;
        return new GateRequest(kind, 0.2f + 0.1f * rng.NextSingle(), kind == GateKind.Blackout ? BlackoutSearchPixels : 0, IsWing: true);
    }

    private sealed record OriginalLevel(MapResourceBuilder Map, LevelModel Model, byte[] Template, IReadOnlyList<string> MusicLines);
}

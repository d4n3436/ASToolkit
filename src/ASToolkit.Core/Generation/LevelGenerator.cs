using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Buildings;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Content;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Generation.Outdoor;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the generation of campaign levels 2 to 10.
/// </summary>
public sealed class LevelGenerator
{
    private const float BreakerSceneReach = 130;
    private const int MaxLayouts = 8;

    // Holds the ambient tint of a locked-doors level, dark until the breaker lights it
    private const uint DarkAmbient = 0x00C0C0C0;

    private const int SeedPrime = 7919;
    private const int BudgetSalt = 17;
    private const int ContentSalt = 101;
    private const int RoofSalt = 211;
    private const int CampaignSalt = 307;
    private const int MaxBreakerRetries = 20;
    private const float GateClearance = 100;
    private const float SiteClearance = 150;
    private const float LaneClearance = 100;
    private const float LaneStep = 40;

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private static readonly string[] BasementSiteLevels = ["Level_03.map", "Level_04.map"];
    private static readonly string[] ConcreteSiteLevels = ["Level_07.map", "Level_08.map", "Level_09.map"];
    private static readonly string[] AddOnBasementSiteLevels = ["ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map"];
    private static readonly string[] AddOnConcreteSiteLevels = ["ADDON/Level_04.map", "ADDON/Level_05.map", "ADDON2/Level_04.map"];

    private readonly StyleModel _model;
    private readonly CampaignPlanner _planner;
    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ReferenceLevelSet _references;
    private readonly ArmyTable _armies;
    private readonly Lazy<OriginalContentSet> _content;
    private readonly Lazy<DressingLibrary> _basementLibrary;
    private readonly Lazy<DressingLibrary> _concreteLibrary;
    private readonly Lazy<RoomFurnisher> _concreteFurnisher;
    private readonly Lazy<RoomFurnisher> _basementFurnisher;
    private readonly Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>> _breakerScenes;
    private readonly Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>> _cannonScenes;
    private readonly Lazy<(Dictionary<byte, List<IReadOnlyList<PropPlacement>>> Marks, float Share)> _missileMarks;
    private readonly OutdoorLevelBuilder _outdoor;
    private readonly Lazy<SiteTemplate[]> _basementSites;
    private readonly Lazy<SiteTemplate[]> _concreteSites;
    private readonly Lazy<KeypadTable> _keypads;
    private readonly ConcurrentDictionary<string, Lazy<OriginalLevel>> _originals = new();
    private readonly ConcurrentDictionary<string, Lazy<EncounterProfile>> _encounters = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelGenerator"/> class.
    /// </summary>
    public LevelGenerator(string gameFolder, ObjectTable objects, ReferenceLevelSet references, StyleModel model)
    {
        _model = model;
        _planner = new CampaignPlanner(model.Campaign);
        _gameFolder = gameFolder;
        _objects = objects;
        _references = references;
        _armies = ArmyTable.Load(references, ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName).Concat(AddOnLevels));
        bool addOnStyle = model.Style == GenerationStyle.AddOn;
        string[] sources = model.FittingLevels.Where(name => name != ReferenceLevelSet.GetCampaignName(1)).ToArray();

        _content = new Lazy<OriginalContentSet>(() => OriginalContentSet.Load(references, sources));
        _basementFurnisher = new Lazy<RoomFurnisher>(() => CreateFurnisher(references, model, objects, WallSet.Basement, sources));
        _concreteFurnisher = new Lazy<RoomFurnisher>(() => CreateFurnisher(references, model, objects, WallSet.Concrete, sources));
        _basementLibrary = new Lazy<DressingLibrary>(() => DressingLibrary.Build(references, model.Relations, objects, WallSet.Basement, sources, GetWallDressing(model, WallSet.Basement),
            model.GetWallClearance(WallSet.Basement), model.GetFacings(WallSet.Basement)));
        _concreteLibrary = new Lazy<DressingLibrary>(() => DressingLibrary.Build(references, model.Relations, objects, WallSet.Concrete, sources, GetWallDressing(model, WallSet.Concrete),
            model.GetWallClearance(WallSet.Concrete), model.GetFacings(WallSet.Concrete)));
        _keypads = new Lazy<KeypadTable>(() => KeypadTable.Load(references, sources));
        _breakerScenes = new Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>>(() => OriginalContent.LoadBreakerScenes(references, Fitted(["Level_02.map", "Level_03.map", "Level_04.map"])));
        _outdoor = new OutdoorLevelBuilder(_armies);
        _cannonScenes = new Lazy<IReadOnlyList<IReadOnlyList<PropPlacement>>>(() => OriginalContent.LoadCannonScenes(references, Fitted(["Level_08.map"])));
        _missileMarks = new Lazy<(Dictionary<byte, List<IReadOnlyList<PropPlacement>>>, float)>(() => OriginalContent.LoadMissileMarks(references, Fitted(["Level_06.map", "Level_07.map", "Level_08.map", "Level_09.map", "Level_10.map"])));
        _basementSites = new Lazy<SiteTemplate[]>(() => OriginalContent.LoadDynamiteSites(references, Fitted(addOnStyle ? BasementSiteLevels.Concat(AddOnBasementSiteLevels).ToArray() : BasementSiteLevels), WallSet.Basement));
        _concreteSites = new Lazy<SiteTemplate[]>(() => OriginalContent.LoadDynamiteSites(references, Fitted(addOnStyle ? ConcreteSiteLevels.Concat(AddOnConcreteSiteLevels).ToArray() : ConcreteSiteLevels), WallSet.Concrete));
    }

    /// <summary>
    /// Generates a level in one pass, drawing its layout again when the tour cannot reach every objective.
    /// </summary>
    public GeneratedLevel Generate(int seed, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, _model.Campaign.LevelCount);
        var generated = Generate(seed, Plan(seed).Levels[level - 1]);
        generated.AddOnNumber = _model.Style == GenerationStyle.AddOn ? level : null;
        return generated;
    }

    /// <summary>
    /// Draws the briefs of the campaign a seed generates.
    /// </summary>
    public CampaignPlan Plan(int seed) => _planner.Plan(new Random(unchecked(seed * SeedPrime + CampaignSalt)));

    /// <summary>
    /// Returns the original map a generated level takes its other chunks from.
    /// </summary>
    public string GetTemplatePath(GeneratedLevel level) => Path.Combine(_gameFolder, "Maps", level.Template);

    // Builds the level on the first layout whose objectives the tour reaches without secret walls or forced gates, else on the layout that misses
    // fewest
    private GeneratedLevel Generate(int seed, LevelBrief brief)
    {
        GeneratedLevel? best = null;
        for (int layoutIndex = 0; layoutIndex < MaxLayouts; layoutIndex++)
        {
            var level = Analyze(Build(GetLayoutSeed(seed, brief.Level, layoutIndex), brief));
            if (level.WallSet == WallSet.Outdoor || level.Analysis?.FallbackUsage is { UsedSecretWalls: false, ForcedGates: false, Unreached: 0 })
            {
                return level;
            }

            if (best is null || GetFallbackRank(level).CompareTo(GetFallbackRank(best)) < 0)
            {
                best = level;
            }
        }

        return best!;
    }

    private static (int Unreached, int ForcedGates, int SecretWalls) GetFallbackRank(GeneratedLevel level)
    {
        var usage = level.Analysis?.FallbackUsage;
        return usage is null ? (int.MaxValue, 1, 1) : (usage.Unreached, usage.ForcedGates ? 1 : 0, usage.UsedSecretWalls ? 1 : 0);
    }

    private (PlacedDecor[] Decor, Dictionary<Cell, int> Blocked) Dress(Random rng, WallSet wallSet, LayoutPlan plan, FineLayout layout, MapBuilder map,
        LevelOccupancy occupancy, IReadOnlySet<KnownObject> held, Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf, HashSet<Cell> floor, HashSet<Cell> keepClear, BreakerSpot? breaker, IReadOnlyDictionary<EdgeKey, EdgeKind> placedEdges, LevelBrief brief,
        MapResourceBuilder originalMap, LevelModel original)
    {
        bool isBasement = wallSet == WallSet.Basement;
        var (wallLattice, walls) = isBasement ? (layout.Lattice, placedEdges) : LevelExtractor.ReadConcreteEdges(map.Sprites) ?? (layout.Lattice, placedEdges);
        var decor = new List<PlacedDecor>();
        AddBreakerScene(decor, layout, occupancy, breaker);
        var clear = new HashSet<Cell>(keepClear);
        if (breaker is not null && plan.KeyRoom is not null)
        {
            clear.UnionWith(layout.GetRegionCells(plan.KeyRoom.Id).Where(cell => IsNearBreaker(layout.Lattice.GetCenter(cell), breaker.Position)));
        }

        var generatorRooms = plan.GeneratorRooms.Count > 0 ? plan.GeneratorRooms.Select(entry => entry.Room).ToArray() : plan.KeyRoom is null ? [] : [plan.KeyRoom];
        foreach (var room in plan.Gate == GateKind.Fences ? generatorRooms : [])
        {
            var entrance = layout.GetDoorwayCell(plan.Links.FirstOrDefault(link => link.To == room.Id)?.Id ?? -1, room.Id);

            var back = layout.GetRegionCells(room.Id)
                .OrderByDescending(cell => Math.Abs(cell.U - entrance.U) + Math.Abs(cell.V - entrance.V))
                .ToArray();

            clear.UnionWith(back.Take(Math.Max(1, back.Length / 3)));
        }

        var gates = map.Sprites.Where(sprite => sprite.Id == KnownObject.FenceEmitter).Select(sprite => layout.Lattice.ToLattice(sprite.Position.X, sprite.Position.Y)).ToArray();
        clear.UnionWith(floor.Where(cell => gates.Any(gate => IsWithin(layout.Lattice, cell, (gate.U, gate.V, GateClearance)))));
        occupancy.Reserve(clear);
        decor.AddRange(Dresser.Dress(rng, isBasement ? _basementLibrary.Value : _concreteLibrary.Value, floor, layout.Edges, keepClear, layout.Lattice, occupancy, held));
        (float corpseRate, float bloodRate) = MeasureGore(originalMap, original);
        RoomDecorator.Decorate(wallSet, plan, layout, decor, floor, keepClear, occupancy, _model.GetDecorHeights(wallSet), _model.GetFacings(wallSet), corpseRate, bloodRate);
        var furnisher = isBasement ? _basementFurnisher.Value : _concreteFurnisher.Value;
        var (_, furniture) = furnisher.Furnish(rng, plan, layout, _references.GetAtlas(brief.DonorLevel), clear, occupancy, companionsOf, wallLattice, walls, _model.GetFacings(wallSet), _model.GetWallClearance(wallSet));
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

    private LevelOccupancy CreateOccupancy(WallSet wallSet, LayoutPlan plan, FineLayout layout, IReadOnlySet<Cell> floor, Cell start)
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
            float? clearance = _model.Relations.GetDoorClearance(wallSet, blocking);
            if (clearance is not null)
            {
                clearances[blocking] = clearance.Value;
            }
        }

        return new LevelOccupancy(_objects, lattice, floor, layout.Edges, start, doorways, clearances,
            (id, other, raised) => _model.Relations.GetSpacing(wallSet, id, other, raised));
    }

    private string[] Fitted(string[] levels) => levels.Where(_model.FittingLevels.Contains).ToArray();

    private static RoomFurnisher CreateFurnisher(ReferenceLevelSet references, StyleModel model, ObjectTable objects, WallSet floorSet, IReadOnlyList<string> sources) =>
        new(RoomRecipe.Measure(sources.Where(name => references.GetSurvey(name).FloorSet == floorSet).SelectMany(name => references.GetAtlas(name).Rooms),
                references.GetSurvey(references.LevelNames.First()).Lattice),
            objects, GetDressingDecor(model, floorSet), references.GetSurvey(references.LevelNames.First()).Lattice);

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

    private EncounterProfile GetEncounters(string name) =>
        _encounters.GetOrAdd(name, key => new Lazy<EncounterProfile>(() => EncounterProfile.Measure(_references.GetSurvey(key), _references.GetLevel(key).Analysis, _objects))).Value;

    // Write a level over its donor, or in add-on style over add-on 1's level of its number
    private string GetTemplateName(LevelBrief brief) => _model.Style == GenerationStyle.AddOn ? $"ADDON/{ReferenceLevelSet.GetCampaignName(brief.Level)}" : brief.DonorLevel;

    private OriginalLevel GetOriginal(string name) => _originals.GetOrAdd(name, key => new Lazy<OriginalLevel>(() => LoadOriginal(key))).Value;

    private OriginalLevel LoadOriginal(string name)
    {
        var reference = _references.GetLevel(name);
        return new OriginalLevel(reference.Map, reference.Model, File.ReadAllBytes(Path.Combine(_gameFolder, "Maps", name)),
            LevelScriptWriter.ReadMusicLines(Path.Combine(_gameFolder, "Maps", Path.ChangeExtension(name, ".lgc"))));
    }

    // Analyze the level's route through its map and script bytes
    private GeneratedLevel Analyze(GeneratedLevel level)
    {
        var map = MapResourceBuilder.Parse(level.Map.ToBytes(GetOriginal(level.Template).Template));
        var script = LevelScriptReader.Parse(LevelScriptWriter.Format(level.Script));
        level.Analysis = RouteAnalyzer.Analyze(level.Template, map, script, _objects);
        return level;
    }

    private static int GetLayoutSeed(int seed, int level, int layoutIndex) => seed * 100_000 + level * 100 + layoutIndex;

    // Draw the layout and structure from the layout seed, and the budget, the decor and content and the roof from streams of their own
    private GeneratedLevel Build(int layoutSeed, LevelBrief brief) =>
        Build(new Random(layoutSeed), new Random(unchecked(layoutSeed * SeedPrime + BudgetSalt)), new Random(unchecked(layoutSeed * SeedPrime + ContentSalt)),
            new Random(unchecked(layoutSeed * SeedPrime + RoofSalt)), brief);

    private GeneratedLevel Build(Random layoutRng, Random budgetRng, Random contentRng, Random roofRng, LevelBrief brief, int retries = 0)
    {
        if (brief.Level == 1)
        {
            return BuildOutdoor(layoutRng, brief);
        }

        var (originalMap, original, _, _) = GetOriginal(brief.DonorLevel);
        string template = GetTemplateName(brief);
        var donor = _references.GetLevel(brief.DonorLevel);
        var wallSet = brief.WallSet;
        var settings = wallSet == WallSet.Basement ? WallSetSettings.Basement : WallSetSettings.Concrete;
        int dynamiteSites = donor.Analysis.Elements.Count(element => element.Kind == ElementKind.DynamiteSite);
        var budget = BudgetPlanner.Plan(budgetRng, brief, dynamiteSites, _content.Value, _objects);
        int spawnClosets = wallSet == WallSet.Basement ? Math.Min(6, budget.Ambushes.Count / 3) : 0;
        var builder = new BuildingBuilder(layoutRng, _model.Footprints);
        var building = builder.Build(new BuildingRequest
        {
            Building = brief.Building,
            Level = brief.Level,
            SecondWing = brief.SecondWing,
            Gate = brief.GateStyle,
            Rooms = brief.Rooms,
            Style = _model.Style,
            IsIntro = brief.IsIntro,
            HasSites = dynamiteSites > 0,
            SecretClosets = wallSet == WallSet.Basement ? brief.SecretClosets : 0,
            SpawnClosets = spawnClosets
        });

        var doors = new DoorStage().Choose(building);
        building = building.DropNecksWithoutDoors(doors.Doors.Keys.ToArray());
        var mission = MissionWriter.Write(doors);
        int pens = wallSet == WallSet.Concrete && brief.GateStyle == LevelStyle.Fenced ? CountPens(donor.Analysis, donor.Survey) : 0;
        var plan = new PlanWriter(wallSet, layoutRng).Write(building, mission, pens);
        var layoutCounts = builder.Counts
            .Concat(doors.Faults.Select(pair => KeyValuePair.Create(pair.Key.ToString(), pair.Value)))
            .ToDictionary();

        var layout = FineLayout.Build(plan);
        StructureBuilder.PlaceLattice(layout, wallSet);
        StructureBuilder.AddFakeDoors(layoutRng, layout, plan);
        var map = new MapBuilder(_armies);
        var closetCells = layout.Cells
            .Where(p => plan.Regions[p.Value].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset)
            .Select(p => p.Key)
            .ToHashSet();

        var floor = layout.Cells.Keys
            .Where(c => !closetCells.Contains(c))
            .ToHashSet();

        var edges = layout.Edges
            .OrderBy(p => p.Key.Axis)
            .ThenBy(p => p.Key.A)
            .ThenBy(p => p.Key.B)
            .ToArray();

        var visibleEdges = edges
            .Where(p => floor.Contains(p.Key.Cells.First) || floor.Contains(p.Key.Cells.Second))
            .ToDictionary();

        var closetEdges = edges
            .Where(p => !visibleEdges.ContainsKey(p.Key) && (closetCells.Contains(p.Key.Cells.First) || closetCells.Contains(p.Key.Cells.Second)))
            .ToDictionary();

        var spawnWalls = wallSet == WallSet.Basement ? StructureBuilder.BuildBasement(layoutRng, map, layout, visibleEdges, contentRng, closetEdges) : [];
        var placedEdges = wallSet == WallSet.Basement ? LevelExtractor.ReadBasementEdges(layout.Lattice, map.Sprites, outdoor: false) : layout.Edges;
        ContentPlacer.PlaceBrokenKeypads(contentRng, map, _keypads.Value, layout.Lattice, _model.GetFacings(wallSet), placedEdges);
        (var doorMarks, float doorMarkShare) = OriginalContent.LoadDoorMarks(originalMap);
        ContentPlacer.PlaceDoorMarks(contentRng, map, layout.Lattice, placedEdges, doorMarks, doorMarkShare);

        if (wallSet == WallSet.Concrete)
        {
            StructureBuilder.BuildConcrete(layoutRng, map, layout, visibleEdges);
        }

        if (plan.Gate == GateKind.Fences)
        {
            foreach (var fence in plan.Links.Where(link => link.Kind == LinkKind.Fence))
            {
                StructureBuilder.BuildFence(map, layout.Lattice, fence.Edges, settings.UnitCells);
            }
        }

        StructureBuilder.BuildRoof(roofRng, wallSet, layout, floor, map);
        map.Source = SpriteSource.Model;

        var startRoom = layout.GetRegionCells(plan.Stations[0].Id).ToArray();
        var start = startRoom[startRoom.Length / 2];
        var breaker = plan.Gate is GateKind.LockedDoors or GateKind.Blackout
            ? ContentPlacer.ChooseBreakerSpot(layoutRng, plan, layout, placedEdges, _breakerScenes.Value, _objects, _model.GetWallClearance(wallSet), _model.GetFacings(wallSet))
            : null;

        if (breaker is null && plan.Gate is GateKind.LockedDoors or GateKind.Blackout && retries < MaxBreakerRetries)
        {
            return Build(layoutRng, budgetRng, contentRng, roofRng, brief, retries + 1);
        }

        var keepClear = GetKeepClear(layout, start, settings.UnitCells);
        var occupancy = CreateOccupancy(wallSet, plan, layout, floor, start);
        occupancy.Reserve(keepClear);
        var held = originalMap.Sprites.Select(sprite => sprite.Id).ToHashSet();
        var companions = new Dictionary<KnownObject, IReadOnlyList<ObjectCompanion>>();
        Func<KnownObject, IReadOnlyList<ObjectCompanion>> companionsOf = anchor => companions.TryGetValue(anchor, out var known)
            ? known
            : companions[anchor] = _model.Relations.GetCompanions(wallSet, anchor).Where(companion => held.Contains(companion.Id)).ToArray();

        var encounters = GetEncounters(brief.DonorLevel);

        // Lay out the cannon and the sites before the decor
        PlacedSites? placedSites = null;
        if (wallSet == WallSet.Concrete)
        {
            var early = new PlacementContext(contentRng, brief, plan, layout, map, _objects, start, new HashSet<Cell>(), keepClear, occupancy, companionsOf);
            placedSites = ContentPlacer.PlaceConcreteSites(early, budget, _concreteSites.Value, _cannonScenes.Value, encounters);
            var siteSpots = GetSiteSpots(map, layout.Lattice, placedSites);
            var siteCells = layout.Cells.Keys.Where(c => siteSpots.Any(spot => IsWithin(layout.Lattice, c, spot))).ToArray();
            keepClear.UnionWith(siteCells);
            occupancy.Reserve(siteCells);
        }

        var (decor, blocked) = Dress(contentRng, wallSet, plan, layout, map, occupancy, held, companionsOf, floor, keepClear, breaker, placedEdges, brief, originalMap, original);
        foreach (var piece in decor)
        {
            (float x, float y) = layout.Lattice.ToWorld(piece.U, piece.V);
            map.Add(piece.Id, x, y, piece.Z, piece.Dir, piece.Payload, unique: false, source: piece.Origin);
        }

        var context = new PlacementContext(contentRng, brief, plan, layout, map, _objects, start, blocked.Keys.ToHashSet(), keepClear, occupancy, companionsOf);
        var player = startRoom
            .Where(c => !blocked.ContainsKey(c) && !layout.IsWalled(c))
            .OrderBy(c => Math.Abs(c.U - start.U) + Math.Abs(c.V - start.V))
            .FirstOrDefault(c => !context.IsInWall(layout.Lattice.GetCenter(c).X, layout.Lattice.GetCenter(c).Y, 20), start);

        (float px, float py) = layout.Lattice.GetCenter(player);

        map.Add(KnownObject.PlayerStart, px, py, 0, 96);
        context.Taken.Add(player);

        var script = new LevelScript
        {
            MusicLines = GetOriginal(template).MusicLines
        };

        ContentPlacer.Place(context, budget, donor.Analysis.Checkpoints.Count > 0, brief.GateStyle == LevelStyle.Blackout, script, breaker, wallSet == WallSet.Basement ? _basementSites.Value : _concreteSites.Value,
            visibleEdges, placedEdges, spawnWalls, _keypads.Value, _model.GetFacings(wallSet), _cannonScenes.Value, encounters, placedSites);

        return new GeneratedLevel
        {
            Level = brief.Level,
            Map = map.ToMap(new Vector2(Math.Max(0, px - 400), Math.Max(0, py - 300)), brief.GateStyle == LevelStyle.LockedDoors ? DarkAmbient : 0),
            Script = script,
            Plan = plan,
            Layout = layout,
            Budget = budget,
            Template = template,
            Building = brief.Building,
            WallSet = wallSet,
            Provenance = map.GetProvenance(),
            LayoutCounts = layoutCounts
        };
    }

    private GeneratedLevel BuildOutdoor(Random rng, LevelBrief brief)
    {
        string name = brief.DonorLevel;
        var style = name.StartsWith("ADDON2/", StringComparison.Ordinal) ? OutdoorStyle.City : name.StartsWith("ADDON/", StringComparison.Ordinal) ? OutdoorStyle.Compound : OutdoorStyle.Campaign;
        var original = _references.GetLevel(name);
        var music = LevelScriptWriter.ReadMusicLines(Path.Combine(_gameFolder, "Maps", Path.ChangeExtension(name, ".lgc")));
        var level = _outdoor.Build(rng, style, original.Map, original.Script.Exit, music);
        return new GeneratedLevel
        {
            Level = 1,
            Map = level.Map,
            Template = name,
            Building = Building.Grounds,
            WallSet = WallSet.Outdoor,
            Script = level.Script,
            Provenance = level.Provenance,
            LayoutCounts = new Dictionary<string, int>()
        };
    }

    // Keep broken keypads for jammed doors
    private static Func<KnownObject, float, bool> GetDressingDecor(StyleModel model, WallSet wallSet) =>
        (id, z) => id != KnownObject.BrokenKeypad && model.GetDecorHeights(wallSet).Allows(id, z);

    // Copy only what hangs on walls or lies on the floor, the furniture being the furnisher's and the missile marks the decorator's
    private static Func<KnownObject, float, bool> GetWallDressing(StyleModel model, WallSet wallSet) =>
        (id, z) => GetDressingDecor(model, wallSet)(id, z) && id.Family is null && !RoomDecorator.IsMissileMark(id, z);

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

    // Count the places holding spawners that open on the rest of the level only through fences
    private static int CountPens(RouteAnalysis analysis, LevelSurvey survey) => analysis.Elements
        .Where(element => element.Kind == ElementKind.Spawner)
        .Select(element => survey.FindPlace(element.X, element.Y))
        .Distinct()
        .Count(place =>
        {
            var links = survey.Connections.Where(connection => connection.A == place || connection.B == place).ToArray();
            return links.Length > 0 && links.All(connection => connection.Kind == ConnectionKind.Fence);
        });

    private sealed record OriginalLevel(MapResourceBuilder Map, LevelModel Model, byte[] Template, IReadOnlyList<string> MusicLines);
}

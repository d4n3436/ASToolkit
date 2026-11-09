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
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the generation of campaign levels 2 to 10.
/// </summary>
public sealed class LevelGenerator
{
    private const int MaxLayouts = 8;

    // Holds the ambient tint of a locked-doors level, dark until the breaker lights it
    private const uint DarkAmbient = 0x00C0C0C0;

    private const int SeedPrime = 7919;
    private const int BudgetSalt = 17;
    private const int ContentSalt = 101;
    private const int RoofSalt = 211;
    private const int CampaignSalt = 307;
    private const int FightSalt = 401;
    private const int MaxBreakerRetries = 20;
    private const float GateClearance = 94;
    private const float SiteClearance = 151;
    private const float BasementSiteClearance = 30;
    private const float GeneratorClearance = 35;
    private const float LaneClearance = 100;
    private const float LaneStep = 40;

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];


    private readonly StyleModel _model;
    private readonly CampaignPlanner _planner;
    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly ReferenceLevelSet _references;
    private readonly ArmyTable _armies;
    private readonly OutdoorLevelBuilder _outdoor;
    private readonly ConcurrentDictionary<string, Lazy<OriginalLevel>> _originals = new();

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
        _outdoor = new OutdoorLevelBuilder(_armies);
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

    private (DecorObject[] Decor, Dictionary<Cell, int> Blocked) Dress(Random rng, WallSet wallSet, LayoutPlan plan, FineLayout layout, MapBuilder map,
        LevelOccupancy occupancy, HashSet<Cell> floor, HashSet<Cell> keepClear, IReadOnlyList<SceneRequest> scenes)
    {
        var clear = new HashSet<Cell>(keepClear);
        var gates = map.Sprites.Where(sprite => sprite.Id == KnownObject.FenceEmitter).Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y)).ToArray();
        clear.UnionWith(floor.Where(cell => gates.Any(gate => IsWithin(layout.Lattice, cell, (gate, GateClearance)))));
        occupancy.Reserve(clear);

        // Give the set pieces, furniture and gore their own random source
        bool rubble = wallSet == WallSet.Basement && rng.NextSingle() < SceneGrammar.Default.RubbleShare;
        var placed = new SceneFurnisher(FurnitureGrammar.Default, _objects, _model.Style, new Random(rng.Next()), layout, occupancy, clear, wallSet, _model.GetWallClearance(wallSet), rubble)
            .Furnish(plan, scenes)
            .ToArray();

        var blocked = new Dictionary<Cell, int>();
        foreach (var piece in placed)
        {
            (float x, float y) = layout.Lattice.ToWorld(piece.U, piece.V);
            foreach (var cell in CollisionBox.GetFootprint(layout.Lattice, _objects, piece.Id, x, y))
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

    // Write a level over its donor, or in add-on style over add-on 1's level of its number
    private string GetTemplateName(LevelBrief brief) => _model.Style == GenerationStyle.AddOn ? $"ADDON/{ReferenceLevelSet.GetCampaignName(brief.Level)}" : brief.DonorLevel;

    private OriginalLevel GetOriginal(string name) => _originals.GetOrAdd(name, key => new Lazy<OriginalLevel>(() => LoadOriginal(key))).Value;

    private OriginalLevel LoadOriginal(string name)
    {
        return new OriginalLevel(File.ReadAllBytes(Path.Combine(_gameFolder, "Maps", name)),
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
            new Random(unchecked(layoutSeed * SeedPrime + RoofSalt)), new Random(unchecked(layoutSeed * SeedPrime + FightSalt)), brief);

    private GeneratedLevel Build(Random layoutRng, Random budgetRng, Random contentRng, Random roofRng, Random fightRng, LevelBrief brief, int retries = 0)
    {
        if (brief.Level == 1)
        {
            return BuildOutdoor(layoutRng, brief);
        }

        string template = GetTemplateName(brief);
        var wallSet = brief.WallSet;
        var settings = wallSet == WallSet.Basement ? WallSetSettings.Basement : WallSetSettings.Concrete;
        int dynamiteSites = brief.DynamiteSites;
        var budget = new BudgetPlanner(_model.Encounters).Plan(budgetRng, brief, dynamiteSites);
        var encounterModel = _model.Encounters;
        int spawnClosets = wallSet == WallSet.Basement && brief.SpawnedShare > 0 ? (int)MathF.Round(brief.Rooms * encounterModel.ClosetsPerRoom.Value) : 0;
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
        var plan = new PlanWriter(wallSet, layoutRng).Write(building, mission);
        var fights = new FightPlanner(encounterModel, FightRule.For(_model.Style), _objects, plan, brief, fightRng).Plan(budget.TargetHitPoints);
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

        var startRoom = layout.GetRegionCells(plan.Stations[0].Id).ToArray();
        var start = startRoom[startRoom.Length / 2];
        var scenes = new ScenePlanner(contentRng, brief, plan, layout, SceneGrammar.Default);
        var sitePlan = scenes.PlanSites(budget.DynamiteSites, _model.Economy, start, visibleEdges);
        var spawnWalls = wallSet == WallSet.Basement
            ? StructureBuilder.BuildBasement(layoutRng, map, layout, visibleEdges, contentRng, closetEdges, closetCells, sitePlan.Holes.Keys.ToHashSet(), plan.Gate == GateKind.LockedDoors)
            : [];
        
        var placedEdges = wallSet == WallSet.Basement ? LevelExtractor.ReadBasementEdges(layout.Lattice, map.Sprites, outdoor: false) : layout.Edges;

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

        var breaker = plan.Gate is GateKind.LockedDoors or GateKind.Blackout
            ? ContentPlacer.ChooseBreakerSpot(layoutRng, plan, layout, placedEdges)
            : null;

        if (breaker is null && plan.Gate is GateKind.LockedDoors or GateKind.Blackout && retries < MaxBreakerRetries)
        {
            return Build(layoutRng, budgetRng, contentRng, roofRng, fightRng, brief, retries + 1);
        }

        var keepClear = GetKeepClear(layout, start, settings.UnitCells);
        var occupancy = CreateOccupancy(wallSet, plan, layout, floor, start);
        occupancy.Reserve(keepClear);
        // Hold the panels the structure hangs beside doors against the furniture and content
        foreach (var sprite in map.Sprites.Where(sprite => sprite.Id.Family is not null))
        {
            occupancy.Add(sprite.Id, new PointF(sprite.Position.X, sprite.Position.Y), sprite.Position.Z);
        }

        bool basement = wallSet == WallSet.Basement;

        // Lay out the cannon and the sites before the decor
        var early = new PlacementContext(contentRng, brief, plan, layout, map, _objects, start, new HashSet<Cell>(), keepClear, occupancy);
        var facings = _model.GetFacings(wallSet);
        var cannon = scenes.PlaceCannon(early, budget);
        scenes.StandBasementSites(map, sitePlan, budget.EndlessGenerators);
        ReserveSiteSpots(map, layout, cannon, basement, keepClear, occupancy);
        var requests = scenes.GetRequests(sitePlan, breaker, cannon, _model.Encounters.GeneratorsPerRoom.Value);
        var (decor, blocked) = Dress(contentRng, wallSet, plan, layout, map, occupancy, floor, keepClear, requests);
        foreach (var piece in decor)
        {
            (float x, float y) = layout.Lattice.ToWorld(piece.U, piece.V);
            map.Add(piece.Id, x, y, piece.Z, piece.Dir, piece.Payload, unique: false, source: SpriteSource.Model);
        }

        if (!basement)
        {
            scenes.StackEndlessGenerators(map, budget.EndlessGenerators);
        }

        ReserveSiteSpots(map, layout, cannon, basement, keepClear, occupancy);

        var context = new PlacementContext(contentRng, brief, plan, layout, map, _objects, start, blocked.Keys.ToHashSet(), keepClear, occupancy);
        var player = startRoom
            .Where(c => !blocked.ContainsKey(c) && !layout.IsWalled(c))
            .OrderBy(c => Math.Abs(c.U - start.U) + Math.Abs(c.V - start.V))
            .FirstOrDefault(c => !context.IsInWall(layout.Lattice.GetCenter(c), 20), start);

        (float px, float py) = layout.Lattice.GetCenter(player);

        map.Add(KnownObject.PlayerStart, px, py, 0, 96);
        context.Taken.Add(player);

        var script = new LevelScript
        {
            MusicLines = GetOriginal(template).MusicLines
        };

        ContentPlacer.Place(context, budget, contentRng.NextSingle() < _model.Economy.CheckpointShare.Value, brief.GateStyle == LevelStyle.Blackout, script, breaker,
            placedEdges, spawnWalls, facings, _model, fights);

        return new GeneratedLevel
        {
            Level = brief.Level,
            Map = map.ToMap(new Vector2(Math.Max(0, px - 400), Math.Max(0, py - 300)), brief.GateStyle == LevelStyle.LockedDoors ? DarkAmbient : 0),
            Script = script,
            Plan = plan,
            Layout = layout,
            Budget = budget,
            Fights = fights,
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

    // Keep the floor clear around every site and pad, and along the line from the cannon to its kill zone
    private static void ReserveSiteSpots(MapBuilder map, FineLayout layout, PlacedCannon? cannon, bool basement, HashSet<Cell> keepClear, LevelOccupancy occupancy)
    {
        var spots = GetSiteSpots(map, cannon, basement ? BasementSiteClearance : SiteClearance);
        var cells = layout.Cells.Keys.Where(cell => spots.Any(spot => IsWithin(layout.Lattice, cell, spot))).ToArray();
        keepClear.UnionWith(cells);
        occupancy.Reserve(cells);
    }

    private static List<(PointF Center, float Reach)> GetSiteSpots(MapBuilder map, PlacedCannon? cannon, float clearance)
    {
        var spots = map.Sprites
            .Where(sprite => sprite.Id is KnownObject.DynamiteSite or KnownObject.TeleportPad or KnownObject.Generator)
            .Select(sprite => (Center: new PointF(sprite.Position.X, sprite.Position.Y), Reach: sprite.Id == KnownObject.Generator ? GeneratorClearance : clearance))
            .ToList();

        if (cannon is null)
            return spots;
        
        var from = cannon.Point;
        var to = cannon.Far;
        int steps = (int)(MathF.Sqrt(MathF.Pow(to.X - from.X, 2) + MathF.Pow(to.Y - from.Y, 2)) / LaneStep);
        for (int step = 0; step <= steps; step++)
        {
            float t = step / (float)Math.Max(1, steps);
            spots.Add((new PointF(from.X + t * (to.X - from.X), from.Y + t * (to.Y - from.Y)), LaneClearance));
        }

        return spots;
    }

    private static bool IsWithin(Lattice lattice, Cell cell, (PointF Center, float Reach) spot)
    {
        (float x, float y) = lattice.GetCenter(cell);
        return (x - spot.Center.X) * (x - spot.Center.X) + (y - spot.Center.Y) * (y - spot.Center.Y) <= spot.Reach * spot.Reach;
    }

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
    private sealed record OriginalLevel(byte[] Template, IReadOnlyList<string> MusicLines);
}

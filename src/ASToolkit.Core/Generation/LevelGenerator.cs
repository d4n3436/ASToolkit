using System.Collections.Concurrent;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Content;
using ASToolkit.Core.Generation.Dressing;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules;

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

    private const int Candidates = 6;
    private const int MaxCandidates = 40;
    private const int MaxBreakerRetries = 20;
    private const int RoofSeed = 99;
    private const double LayoutFillSlack = 0.9;

    // Campaign levels the add-on levels take their structure from
    private static readonly int[] AddOnSlots = [2, 3, 5, 8, 10];

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

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
    private readonly Lazy<IReadOnlyList<PropPlacement>[]> _breakerScenes;
    private readonly Lazy<SiteTemplate[]> _labSites;
    private readonly Lazy<SiteTemplate[]> _militarySites;
    private readonly Lazy<SpriteLibrary> _sprites;
    private readonly Lazy<KeypadTable> _keypads;
    private readonly ConcurrentDictionary<int, Lazy<OriginalLevel>> _originals = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelGenerator"/> class.
    /// </summary>
    public LevelGenerator(string gameFolder, ObjectTable objects, ReferenceLevelSet references, ArmyTable armies, bool addOnStyle = false)
    {
        _addOnStyle = addOnStyle;
        _gameFolder = gameFolder;
        _objects = objects;
        _references = references;
        _armies = armies;
        string[] sources = ReferenceLevelSet.CampaignLevels
            .Select(ReferenceLevelSet.GetCampaignName)
            .Concat(addOnStyle ? AddOnLevels : [])
            .ToArray();
        
        _content = new Lazy<OriginalContentSet>(() => OriginalContentSet.Load(gameFolder, sources, objects));
        _labLibrary = new Lazy<DressingLibrary>(() => DressingLibrary.Build(gameFolder, objects, LevelTheme.Lab, sources, GetDressingDecor(references, LevelTheme.Lab), references.GetWallClearance(LevelTheme.Lab), references.GetFacings(LevelTheme.Lab)));
        _militaryLibrary = new Lazy<DressingLibrary>(() => DressingLibrary.Build(gameFolder, objects, LevelTheme.Military, sources, GetDressingDecor(references, LevelTheme.Military), references.GetWallClearance(LevelTheme.Military), references.GetFacings(LevelTheme.Military)));
        _sprites = new Lazy<SpriteLibrary>(() => new SpriteLibrary(gameFolder, objects));
        _keypads = new Lazy<KeypadTable>(() => KeypadTable.Load(gameFolder, sources));
        _breakerScenes = new Lazy<IReadOnlyList<PropPlacement>[]>(() => OriginalContent.LoadBreakerScenes(gameFolder, ["Level_02.map", "Level_03.map", "Level_04.map"]));
        _labSites = new Lazy<SiteTemplate[]>(() => OriginalContent.LoadDynamiteSites(gameFolder, addOnStyle ? LabSiteLevels.Concat(AddOnLabSiteLevels).ToArray() : LabSiteLevels, LevelTheme.Lab));
        _militarySites = new Lazy<SiteTemplate[]>(() => OriginalContent.LoadDynamiteSites(gameFolder, addOnStyle ? MilitarySiteLevels.Concat(AddOnMilitarySiteLevels).ToArray() : MilitarySiteLevels, LevelTheme.Military));
    }

    /// <summary>
    /// Returns the original map a generated level takes its other chunks from.
    /// </summary>
    public string GetTemplatePath(int level) => Path.Combine(_gameFolder, "Maps", ReferenceLevelSet.GetCampaignName(level));

    /// <summary>
    /// Generates candidates of a level and returns the one that breaks no hard rule and meets the most soft rules, or the
    /// one that breaks the fewest hard rules when none passes.
    /// </summary>
    public GeneratedLevel Generate(int seed, int level) => Generate(seed, level, _references.GetTarget(level));

    /// <summary>
    /// Generates an add-on level, numbered 1 to 5. It takes its structure from a campaign level, and its monsters and
    /// pickups from the same level of one of the two add-ons, whose hit points it matches on average. It needs the
    /// generator to have been created in add-on style.
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
        int hitPoints = (int)Math.Round(Math.Sqrt(names.Aggregate(1.0, (product, n) => product * Math.Max(1, _content.Value.GetLevel(n).HitPoints))));
        var target = _references.GetTarget(AddOnSlots[number - 1], hitPoints, names[Math.Abs(seed) % 2]);
        var level = Generate(seed, AddOnSlots[number - 1], target);
        level.AddOnNumber = number;
        return level;
    }

    /// <summary>
    /// Returns the original map an add-on level takes its other chunks from.
    /// </summary>
    public string GetAddOnTemplatePath(int number) => Path.Combine(_gameFolder, "Maps", $"ADDON/Level_{number:00}.map");

    private GeneratedLevel Generate(int seed, int level, LevelTarget target)
    {
        GeneratedLevel? best = null;
        (int Hard, int Soft) bestScore = (int.MaxValue, -1);
        for (int first = 0; first < MaxCandidates && !(first >= Candidates && bestScore.Hard == 0); first += Candidates)
        {
            int start = first;
            var batch = new (GeneratedLevel Level, RuleResult[] Results)[Math.Min(Candidates, MaxCandidates - first)];
            Parallel.For(0, batch.Length, i =>
            {
                int k = start + i;
                var built = Build(new Random(seed * 100_000 + level * 100 + k), level, target);
                batch[i] = (built, Check(built, target));
            });

            for (int batchIndex = 0; batchIndex < batch.Length; batchIndex++)
            {
                if (first + batchIndex >= Candidates && bestScore.Hard == 0)
                {
                    break;
                }

                var (candidate, results) = batch[batchIndex];
                (int Hard, int Soft) score = (results.Count(r => r is { Severity: RuleSeverity.Hard, Passed: false }), results.Count(r => r is { Severity: RuleSeverity.Soft, Passed: true }));
                if (score.Hard < bestScore.Hard || (score.Hard == bestScore.Hard && score.Soft > bestScore.Soft))
                {
                    best = candidate;
                    bestScore = score;
                    best.Results = results;
                }
            }
        }

        best!.RoofResidual = RoofBuilder.Build(new Random(seed * 100_000 + level * 100 + RoofSeed), best.Builder, best.Layout.Lattice, best.Shown,
            best.Layout.Cells.Keys.Where(c => !best.Shown.Contains(c)).ToHashSet(), best.Theme, _sprites.Value);
        best.Map = best.Builder.ToMap(best.Map.Scroll);
        return best;
    }

    private OriginalLevel GetOriginal(int level) => _originals.GetOrAdd(level, l => new Lazy<OriginalLevel>(() => LoadOriginal(l))).Value;

    private OriginalLevel LoadOriginal(int level)
    {
        string name = ReferenceLevelSet.GetCampaignName(level);
        byte[] template = File.ReadAllBytes(GetTemplatePath(level));
        var map = GameMap.Parse(template);
        var model = LevelExtractor.Extract(name, map, _objects) ?? throw new InvalidDataException($"{name} has no floor.");
        return new OriginalLevel(map, model, template, LevelScriptWriter.ReadMusicLines(Path.Combine(_gameFolder, "Maps", Path.ChangeExtension(name, ".lgc"))));
    }

    // Check the level through its map and script bytes
    private RuleResult[] Check(GeneratedLevel level, LevelTarget target)
    {
        var map = GameMap.Parse(level.Map.ToBytes(GetOriginal(level.Level).Template));
        var script = LevelScriptReader.Parse(LevelScriptWriter.Format(level.Script));
        var analysis = RouteAnalyzer.Analyze(ReferenceLevelSet.GetCampaignName(level.Level), map, script, _objects);
        if (analysis is null)
        {
            return [new RuleResult("A2", RuleSeverity.Hard, false, "no route")];
        }

        level.Analysis = analysis;
        return RuleSet.Default.Evaluate(RuleContext.Create(map, script, analysis, _objects, target));
    }

    private GeneratedLevel Build(Random rng, int level, LevelTarget target, int retries = 0)
    {
        var (originalMap, original, _, musicLines) = GetOriginal(level);
        var theme = original.Theme;
        var settings = theme == LevelTheme.Lab ? ThemeSettings.Lab : ThemeSettings.Military;
        int originalUnits = theme == LevelTheme.Lab ? original.Cells.Count : original.Cells.Count / 4;
        var gate = ChooseGate(rng, target, theme);
        var budget = BudgetPlanner.Plan(rng, level, target, _content.Value, _objects);
        int spawnClosets = theme == LevelTheme.Lab ? Math.Min(6, budget.Ambushes.Count / 3) : 0;
        int shortcuts = theme == LevelTheme.Lab ? rng.Next(3) : 0;
        // Use the original's elongation for blackouts and its mean with the add-ons' otherwise
        double elongation = gate is { Kind: GateKind.Blackout, IsWing: false } ? target.OriginalElongation : (target.Elongation + target.OriginalElongation) / 2;
        double maxElongation = theme == LevelTheme.Lab ? Math.Max(target.Elongation, target.OriginalElongation) : double.PositiveInfinity;
        LayoutPlan? plan = null;
        for (int attempt = 0; plan is null; attempt++)
        {
            if (attempt == 200)
            {
                throw new InvalidOperationException($"No layout for level {level}.");
            }

            int targetUnits = (int)(originalUnits * (0.9 + 0.15 * rng.NextDouble()));
            plan = LayoutPlanner.Plan(rng, settings, targetUnits, target.BoxFill * LayoutFillSlack, elongation, maxElongation, gate, theme == LevelTheme.Lab ? target.SecretClosets : 0, spawnClosets, shortcuts);
        }

        var layout = FineLayout.Build(plan);
        StructureBuilder.PlaceLattice(layout, theme);
        StructureBuilder.AddFakeDoors(rng, layout, plan);
        var map = new MapBuilder(_armies);
        var closetCells = layout.Cells.Where(p => plan.Regions[p.Value].Kind is RegionKind.SecretCloset or RegionKind.SpawnCloset).Select(p => p.Key).ToHashSet();
        var shown = layout.Cells.Keys.Where(c => !closetCells.Contains(c)).ToHashSet();
        var visibleEdges = layout.Edges
            .Where(p => shown.Contains(p.Key.Cells.First) || shown.Contains(p.Key.Cells.Second))
            .OrderBy(p => p.Key.Axis).ThenBy(p => p.Key.A).ThenBy(p => p.Key.B)
            .ToDictionary(p => p.Key, p => p.Value);
        var spawnWalls = theme == LevelTheme.Lab ? StructureBuilder.BuildLab(rng, map, layout, visibleEdges) : [];
        ContentPlacer.PlaceBrokenKeypads(rng, map, _keypads.Value);
        if (theme == LevelTheme.Military)
        {
            StructureBuilder.BuildMilitary(rng, map, layout, visibleEdges);
        }

        if (plan is { Gate: GateKind.Fences, GateLink: not null })
        {
            StructureBuilder.BuildFence(map, layout.Lattice, plan.GateLink.Edges, settings.UnitCells);
        }


        var startRoom = layout.GetRegionCells(plan.Stations[0].Id).ToArray();
        var start = startRoom[startRoom.Length / 2];
        var breaker = plan.Gate is GateKind.LockedDoors or GateKind.Blackout
            ? ContentPlacer.ChooseBreakerSpot(rng, plan, layout, _breakerScenes.Value, _objects, target.WallClearance, target.Facings)
            : null;
        if (breaker is null && plan.Gate is GateKind.LockedDoors or GateKind.Blackout && retries < MaxBreakerRetries)
        {
            return Build(rng, level, target, retries + 1);
        }

        var floor = layout.Cells.Keys.Where(c => !closetCells.Contains(c)).ToHashSet();
        var decor = Dresser.Dress(rng, theme == LevelTheme.Lab ? _labLibrary.Value : _militaryLibrary.Value, floor, layout.Edges).ToList();
        var keepClear = GetKeepClear(layout, start, settings.UnitCells);
        if (breaker is not null && plan.KeyRoom is not null)
        {
            var room = layout.GetRegionCells(plan.KeyRoom.Id).ToHashSet();
            decor = decor.Where(o => !room.Contains(new Cell((int)Math.Floor(o.U), (int)Math.Floor(o.V)))).ToList();
            for (int propIndex = 0; propIndex < breaker.Props.Count; propIndex++)
            {
                var p = breaker.Props[propIndex];
                (double u, double v) = layout.Lattice.ToLattice(breaker.X + p.Dx, breaker.Y + p.Dy);
                decor.Add(new PlacedDecor(p.Id, u, v, p.Z, p.Dir, new byte[8], (-1, propIndex), null));
            }
        }

        var (kept, _) = Dresser.ClearAndConnect(decor, floor, layout.Edges, keepClear, start, layout.Lattice, _objects);
        var excluded = new HashSet<int> { plan.Stations[0].Id };
        if (budget.Bosses.Count > 0)
        {
            excluded.Add(plan.Stations[^1].Id);
        }

        if (plan.KeyRoom is not null)
        {
            excluded.Add(plan.KeyRoom.Id);
        }

        var clutterCells = floor.Where(c => !excluded.Contains(layout.Cells[c])).ToHashSet();
        var (solid, breakable) = ClutterPlanner.GetPalette(originalMap, _objects);
        double blockedShare = target.BlockedShare * (0.95 + 0.2 * rng.NextDouble());
        kept = ClutterPlanner.AddClutter(rng, kept, floor, clutterCells, layout.Edges, keepClear, start, layout.Lattice, _objects, solid, breakable, blockedShare, target.Breakables, target.WallClearance, target.Facings);
        (kept, var blocked) = Dresser.ClearAndConnect(kept, floor, layout.Edges, keepClear, start, layout.Lattice, _objects);
        foreach (var keptObject in kept)
        {
            (double x, double y) = layout.Lattice.ToWorld(keptObject.U, keptObject.V);
            map.Add(keptObject.Id, x, y, keptObject.Z, keptObject.Dir, keptObject.Payload, unique: false);
        }

        var context = new PlacementContext(rng, level, plan, layout, map, _objects, start, blocked.Keys.ToHashSet(), keepClear);
        var player = startRoom
            .Where(c => !blocked.ContainsKey(c) && !layout.IsWalled(c))
            .OrderBy(c => Math.Abs(c.U - start.U) + Math.Abs(c.V - start.V))
            .FirstOrDefault(c => !context.IsInWall(layout.Lattice.GetCenter(c).X, layout.Lattice.GetCenter(c).Y, 20), start);
        (double px, double py) = layout.Lattice.GetCenter(player);
        map.Add(KnownObject.PlayerStart, px, py, 0, 96);
        context.Taken.Add(player);

        var script = new LevelScript
        {
            MusicLines = musicLines
        };
        ContentPlacer.Place(context, budget, target.HasCheckpoint, target.Style == LevelStyle.Blackout, script, breaker, theme == LevelTheme.Lab ? _labSites.Value : _militarySites.Value, visibleEdges, spawnWalls, _keypads.Value, target.Facings);
        return new GeneratedLevel
        {
            Level = level,
            Map = map.ToMap((Math.Max(0, px - 400), Math.Max(0, py - 300))),
            Script = script,
            Plan = plan,
            Layout = layout,
            Budget = budget,
            Builder = map,
            Shown = shown,
            Theme = theme
        };
    }

    // Keep broken keypads for jammed doors
    private static Func<KnownObject, double, bool> GetDressingDecor(ReferenceLevelSet references, LevelTheme theme) =>
        (id, z) => id != KnownObject.BrokenKeypad && references.GetThemeDecor(theme).Allows(id, z);

    // Keep two cells clear on both sides of every doorway and around the player start
    private static HashSet<Cell> GetKeepClear(FineLayout layout, Cell start, int unitCells)
    {
        var keep = new HashSet<Cell>();
        foreach (var edges in layout.LinkEdges)
        {
            foreach (var edge in edges)
            {
                foreach (var cell in new[] { edge.Cells.First, edge.Cells.Second })
                {
                    for (int index = -1; index <= unitCells; index++)
                    {
                        for (int innerIndex = -1; innerIndex <= unitCells; innerIndex++)
                        {
                            keep.Add(new Cell(cell.U + index, cell.V + innerIndex));
                        }
                    }
                }
            }
        }

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
        LevelStyle.LockedDoors => new GateRequest(GateKind.LockedDoors, target.LockedShare ?? 0.5, 0),
        LevelStyle.Blackout => new GateRequest(GateKind.Blackout, 0, 5200),
        LevelStyle.Fenced => new GateRequest(GateKind.Fences, target.FencedOffShare ?? 0.4, 0),
        _ => theme == LevelTheme.Military
            ? new GateRequest(GateKind.Fences, 0.2 + 0.1 * rng.NextDouble(), 0, IsWing: true)
            : new GateRequest(rng.Next(2) == 0 ? GateKind.LockedDoors : GateKind.Blackout, 0.2 + 0.1 * rng.NextDouble(), 0, IsWing: true)
    };

    private sealed record OriginalLevel(GameMap Map, LevelModel Model, byte[] Template, IReadOnlyList<string> MusicLines);
}

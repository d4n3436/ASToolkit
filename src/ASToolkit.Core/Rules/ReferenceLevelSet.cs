using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules.Checks;

namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents the game's own levels analyzed, and the targets they set for generated levels.
/// </summary>
public sealed class ReferenceLevelSet
{
    /// <summary>
    /// Represents the weight of the add-ons in the tour length mix.
    /// </summary>
    public const float AddOnWeight = 2.0f / 3.0f;

    /// <summary>
    /// Represents how much more monster hit points a generated level has than the campaign level it replaces.
    /// </summary>
    public const float HitPointFactor = 1.15f;

    private static readonly string[] AddOn1 = ["ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map"];
    private static readonly string[] AddOn2 = ["ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"];

    private readonly Dictionary<string, (RouteAnalysis Analysis, LevelMeasurement Measures)> _levels;
    private readonly Dictionary<LevelTheme, DecorHeightTable> _campaignDecor;
    private readonly Dictionary<LevelTheme, WallClearanceTable> _wallClearance;
    private readonly Dictionary<LevelTheme, FacingTable> _facings;
    private readonly Dictionary<string, LevelModel> _models;

    private ReferenceLevelSet(Dictionary<string, (RouteAnalysis, LevelMeasurement)> levels, Dictionary<LevelTheme, DecorHeightTable> campaignDecor,
        Dictionary<LevelTheme, WallClearanceTable> wallClearance, Dictionary<LevelTheme, FacingTable> facings, Dictionary<string, LevelModel> models)
    {
        _facings = facings;
        _models = models;
        _levels = levels;
        _campaignDecor = campaignDecor;
        _wallClearance = wallClearance;
    }

    /// <summary>
    /// Gets the campaign levels the generator replaces.
    /// </summary>
    public static IReadOnlyList<int> CampaignLevels { get; } = [2, 3, 4, 5, 6, 7, 8, 9, 10];

    /// <summary>
    /// Returns the file name of a campaign level.
    /// </summary>
    public static string GetCampaignName(int level) => $"Level_{level:00}.map";

    /// <summary>
    /// Analyzes the campaign levels 2 to 10 and the fighting levels of both add-ons; in add-on style the add-ons' decor is allowed too.
    /// </summary>
    public static ReferenceLevelSet Load(string gameFolder, ObjectTable objects, bool addOnStyle = false)
    {
        var levels = new Dictionary<string, (RouteAnalysis, LevelMeasurement)>();
        var campaignDecor = new Dictionary<LevelTheme, DecorHeightTable> { [LevelTheme.Lab] = new(), [LevelTheme.Military] = new() };
        var campaign = CampaignLevels.Select(GetCampaignName).ToHashSet();
        var models = new Dictionary<string, LevelModel>();
        string[] names = campaign
            .Concat(AddOn1)
            .Concat(AddOn2)
            .ToArray();
        
        var loaded = new (MapResourceBuilder Map, RouteAnalysis Analysis, LevelMeasurement Measures, LevelModel? Model)[names.Length];
        Parallel.For(0, names.Length, i =>
        {
            string name = names[i];
            var map = MapResourceBuilder.Load(Path.Combine(gameFolder, "Maps", name));
            var script = LevelScriptReader.Read(Path.Combine(gameFolder, "Maps", Path.ChangeExtension(name, ".lgc")));
            var analysis = RouteAnalyzer.Analyze(name, map, script, objects)
                           ?? throw new InvalidDataException($"{name} has no route.");
            loaded[i] = (map, analysis, LevelMeasurer.Measure(map, script, analysis, objects), LevelExtractor.Extract(name, map, objects));
        });

        for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            string name = names[nameIndex];
            var (map, analysis, measures, model) = loaded[nameIndex];
            levels[name] = (analysis, measures);
            if (model is not null)
            {
                models[name] = model;
            }

            if (addOnStyle || campaign.Contains(name))
            {
                foreach (var sprite in map.Sprites.Where(s => s.Id.IsDecor))
                {
                    campaignDecor[analysis.Theme].Add(sprite.Id, sprite.Position.Z);
                }
            }
        }

        var wallClearance = new[] { LevelTheme.Lab, LevelTheme.Military }
            .ToDictionary(t => t, t => WallClearanceTable.Build(models.Values.Where(m => m.Theme == t)));
        var leaning = FacingTable.FindLeaningFrames(models.Values, objects);
        var facings = new[] { LevelTheme.Lab, LevelTheme.Military }
            .ToDictionary(t => t, t => FacingTable.Build(models.Values.Where(m => m.Theme == t), objects, leaning));
        return new ReferenceLevelSet(levels, campaignDecor, wallClearance, facings, models);
    }

    /// <summary>
    /// Returns the analysis and measures of a reference level.
    /// </summary>
    public (RouteAnalysis Analysis, LevelMeasurement Measures) GetLevel(string name) => _levels[name];

    /// <summary>
    /// Returns the decor the campaign uses on levels of a theme, with the heights it places each at.
    /// </summary>
    public DecorHeightTable GetThemeDecor(LevelTheme theme) => _campaignDecor[theme];

    /// <summary>
    /// Returns how close the originals of a theme place each decor object to a wall.
    /// </summary>
    public WallClearanceTable GetWallClearance(LevelTheme theme) => _wallClearance[theme];

    /// <summary>
    /// Returns how the original levels of a theme turn decor against walls.
    /// </summary>
    public FacingTable GetFacings(LevelTheme theme) => _facings[theme];

    /// <summary>
    /// Returns the targets for a generated campaign level, with the hit points and the level whose pickups it copies replaced when given.
    /// </summary>
    public LevelTarget GetTarget(int level, int? hitPoints = null, string? contentLevel = null)
    {
        var (a, m) = _levels[GetCampaignName(level)];
        int? walkToBreaker = m.Style == LevelStyle.LockedDoors && a.Legs.Count > 0 && a.Legs[0].Phase == RoutePhase.DoorsLocked ? a.Legs[0].Pixels : null;
        return new LevelTarget
        {
            ContentLevel = contentLevel ?? GetCampaignName(level),
            Level = level,
            Style = m.Style,
            BlockedShare = m.BlockedShare,
            Breakables = m.Breakables,
            ReferenceTourPixels = (int)MathF.Round(Blend(level, n => _levels[n].Analysis.RoutePixels)),
            RevisitCeiling = Math.Clamp(a.RevisitShare + 0.03f, 0.06f, 0.16f),
            HitPoints = hitPoints ?? GetHitPointTarget(level),
            LockedShare = m.Style == LevelStyle.LockedDoors ? a.Gating.LockedShare : null,
            FencedOffShare = a.Gating.FencedOffShare,
            WalkToBreakerPixels = walkToBreaker,
            HasCheckpoint = a.Checkpoints.Count > 0,
            DynamiteSites = a.Elements.Count(e => e.Kind == ElementKind.DynamiteSite),
            SecretClosets = m.SecretClosets.Count,
            HasBoss = a.Elements.Any(e => e.Kind == ElementKind.Boss),
            HasMountedCannon = a.Elements.Any(e => e.Kind == ElementKind.MountedCannon),
            AmbushChance = Math.Clamp(m.SpawnedShare, 0.05f, 0.95f),
            PlacedMonsterProgress = PlacedGroupRule.GetMedianProgress(a),
            Weapons = (contentLevel is not null && _levels.TryGetValue(contentLevel, out var content) ? content.Analysis : a).Elements.Count(e => e.Kind is ElementKind.Pickup or ElementKind.Crate && e.Category == ItemCategory.Weapon),
            Generators = a.Elements.Count(e => e.Kind == ElementKind.Generator),
            HasGeneratorAmbush = GeneratorRule.HasAmbushAtGenerator(a),
            ThemeDecor = GetThemeDecor(a.Theme),
            WallClearance = GetWallClearance(a.Theme),
            Facings = GetFacings(a.Theme),
            BoxFill = _models[GetCampaignName(level)].GetBoxFill(),
            BreakerWallRun = a.Elements.Where(e => e.Kind == ElementKind.Breaker)
                .Select(e => BreakerRoomRule.MeasureWallRun(_models[GetCampaignName(level)], e.X, e.Y)).FirstOrDefault(),
            SpineShare = Blend(level, n => GetSpineShare(_levels[n].Analysis)),
            OriginalSpineShare = GetSpineShare(a),
            Elongation = Blend(level, n => GetElongation(_levels[n].Analysis)),
            OriginalElongation = GetElongation(a)
        };
    }

    private static float GetSpineShare(RouteAnalysis a) => a.SpinePixels / (float)Math.Max(1, a.RoutePixels);

    /// <summary>
    /// Returns the walk from the start to the farthest point of the tour over the square root of the floor area.
    /// </summary>
    public static float GetElongation(RouteAnalysis a) => a.SpinePixels / (a.Step * MathF.Sqrt(Math.Max(1, a.ReachableCells)));

    private int GetHitPointTarget(int level) => (int)MathF.Round(_levels[GetCampaignName(level)].Measures.HitPoints * HitPointFactor);

    // Interpolate each add-on's four fighting levels geometrically over campaign levels 2 to 10
    private float Blend(int level, Func<string, float> value)
    {
        float t = (level - 2) / 8.0f * 3;
        int i = Math.Min(2, (int)t);
        float f = t - i;
        float addOn = MathF.Sqrt(Interpolate(AddOn1) * Interpolate(AddOn2));
        float campaign = value(GetCampaignName(level));
        return MathF.Exp(MathF.Log(campaign) * (1 - AddOnWeight) + MathF.Log(addOn) * AddOnWeight);
        
        float Interpolate(string[] addOnLevels) => MathF.Exp(MathF.Log(value(addOnLevels[i])) * (1 - f) + MathF.Log(value(addOnLevels[i + 1])) * f);
    }
}

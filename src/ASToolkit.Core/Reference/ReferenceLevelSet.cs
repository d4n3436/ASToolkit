using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the game's own levels analyzed, and the targets they set for generated levels.
/// </summary>
public sealed class ReferenceLevelSet
{
    /// <summary>
    /// Represents how much more monster hit points a generated level has than the campaign level it replaces.
    /// </summary>
    public const float HitPointFactor = 1.15f;

    private static readonly string[] AddOn1 = ["ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map"];
    private static readonly string[] AddOn2 = ["ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"];
    private static readonly string[] FirstLevels = ["Level_01.map", "ADDON/Level_01.map", "ADDON2/Level_01.map"];
    private static readonly LevelTheme[] Themes = [LevelTheme.Lab, LevelTheme.Military, LevelTheme.Outdoor];

    private readonly Dictionary<string, (RouteAnalysis Analysis, LevelMeasurement Measures)> _levels;
    private readonly Dictionary<LevelTheme, DecorHeightTable> _campaignDecor;
    private readonly Dictionary<LevelTheme, WallClearanceTable> _wallClearance;
    private readonly Dictionary<LevelTheme, FacingTable> _facings;
    private readonly Dictionary<string, LevelModel> _models;
    private readonly Dictionary<string, LevelSurvey> _surveys;
    private readonly Dictionary<string, DesignMeasures> _designs;
    private readonly Dictionary<string, RoomAtlas> _atlases;

    private ReferenceLevelSet(Dictionary<string, (RouteAnalysis, LevelMeasurement)> levels, Dictionary<LevelTheme, DecorHeightTable> campaignDecor,
        Dictionary<LevelTheme, WallClearanceTable> wallClearance, Dictionary<LevelTheme, FacingTable> facings, Dictionary<string, LevelModel> models,
        Dictionary<string, LevelSurvey> surveys, Dictionary<string, DesignMeasures> designs, Dictionary<string, RoomAtlas> atlases)
    {
        _atlases = atlases;
        _surveys = surveys;
        _designs = designs;
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
    /// Analyzes the campaign levels, the fighting levels of both add-ons and the three level 1 maps; in add-on style the add-ons' decor
    /// is allowed too.
    /// </summary>
    public static ReferenceLevelSet Load(string gameFolder, ObjectTable objects, bool addOnStyle = false)
    {
        var levels = new Dictionary<string, (RouteAnalysis, LevelMeasurement)>();
        var campaignDecor = Themes.ToDictionary(theme => theme, _ => new DecorHeightTable());
        var campaign = CampaignLevels.Select(GetCampaignName).ToHashSet();
        var models = new Dictionary<string, LevelModel>();
        var surveys = new Dictionary<string, LevelSurvey>();
        var designs = new Dictionary<string, DesignMeasures>();
        var atlases = new Dictionary<string, RoomAtlas>();
        string[] names = campaign
            .Concat(FirstLevels)
            .Concat(AddOn1)
            .Concat(AddOn2)
            .ToArray();
        
        var loaded = new (MapResourceBuilder Map, RouteAnalysis Analysis, LevelMeasurement Measures, LevelModel? Model, LevelSurvey Survey, DesignMeasures? Design, RoomAtlas Atlas)[names.Length];
        Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            string name = names[i];
            var map = MapResourceBuilder.Load(Path.Combine(gameFolder, "Maps", name));
            var script = LevelScriptReader.Read(Path.Combine(gameFolder, "Maps", Path.ChangeExtension(name, ".lgc")));
            var analysis = RouteAnalyzer.Analyze(name, map, script, objects)
                           ?? throw new InvalidDataException($"{name} has no route.");
            var model = LevelExtractor.Extract(name, map, objects);
            var survey = LevelSurveyor.Survey(map, script, analysis, objects);
            var design = model is null ? null : DesignMeasurer.Measure(model, analysis, survey, objects);
            loaded[i] = (map, analysis, LevelMeasurer.Measure(map, script, analysis, objects), model, survey, design, RoomAtlas.Read(survey, analysis.Grid.Edges, map.Sprites));
        });

        for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
        {
            string name = names[nameIndex];
            var (map, analysis, measures, model, survey, design, atlas) = loaded[nameIndex];
            levels[name] = (analysis, measures);
            atlases[name] = atlas;
            surveys[name] = survey;
            if (design is not null)
            {
                designs[name] = design;
            }

            if (model is not null)
            {
                models[name] = model;
            }

            if (addOnStyle || campaign.Contains(name) || name == FirstLevels[0])
            {
                foreach (var sprite in map.Sprites.Where(s => s.Id.IsDecor))
                {
                    campaignDecor[analysis.Theme].Add(sprite.Id, sprite.Position.Z);
                }
            }
        }

        var wallClearance = Themes
            .ToDictionary(t => t, t => WallClearanceTable.Build(models.Values.Where(m => m.Theme == t)));
        var leaning = FacingTable.FindLeaningFrames(models.Values, objects);
        var facings = Themes
            .ToDictionary(t => t, t => FacingTable.Build(models.Values.Where(m => m.Theme == t), objects, leaning));
        return new ReferenceLevelSet(levels, campaignDecor, wallClearance, facings, models, surveys, designs, atlases);
    }

    /// <summary>
    /// Returns the analysis and measures of a reference level.
    /// </summary>
    public (RouteAnalysis Analysis, LevelMeasurement Measures) GetLevel(string name) => _levels[name];

    /// <summary>
    /// Returns the design measures of a reference level.
    /// </summary>
    public DesignMeasures GetDesign(string name) => _designs[name];

    /// <summary>
    /// Gets the names of the reference levels.
    /// </summary>
    public IReadOnlyCollection<string> LevelNames => _surveys.Keys;

    /// <summary>
    /// Returns the survey of a reference level.
    /// </summary>
    public LevelSurvey GetSurvey(string name) => _surveys[name];

    /// <summary>
    /// Returns the rooms of a reference level with their roles and types.
    /// </summary>
    public RoomAtlas GetAtlas(string name) => _atlases[name];

    /// <summary>
    /// Returns the model of a reference level.
    /// </summary>
    public LevelModel GetModel(string name) => _models[name];

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
    /// That level also sets the design targets when it is a reference level, and the floor furniture blocks on a lab level.
    /// </summary>
    public LevelTarget GetTarget(int level, int? hitPoints = null, string? contentLevel = null)
    {
        var (a, m) = _levels[GetCampaignName(level)];
        string designName = contentLevel is not null && _designs.ContainsKey(contentLevel) ? contentLevel : GetCampaignName(level);
        int? walkToBreaker = m.Style == LevelStyle.LockedDoors && a.Legs.Count > 0 && a.Legs[0].Phase == RoutePhase.DoorsLocked ? a.Legs[0].Pixels : null;
        return new LevelTarget
        {
            ContentLevel = contentLevel ?? GetCampaignName(level),
            Style = m.Style,
            HitPoints = hitPoints ?? GetHitPointTarget(level),
            LockedShare = m.Style == LevelStyle.LockedDoors ? a.Gating.LockedShare : null,
            FencedOffShare = a.Gating.FencedOffShare,
            WalkToBreakerPixels = walkToBreaker,
            HasCheckpoint = a.Checkpoints.Count > 0,
            DynamiteSites = a.Elements.Count(e => e.Kind == ElementKind.DynamiteSite),
            SecretClosets = m.SecretClosets.Count,
            Weapons = (contentLevel is not null && _levels.TryGetValue(contentLevel, out var content) ? content.Analysis : a).Elements.Count(e => e.Kind is ElementKind.Pickup or ElementKind.Crate && e.Category == ItemCategory.Weapon),
            ThemeDecor = GetThemeDecor(a.Theme),
            WallClearance = GetWallClearance(a.Theme),
            Facings = GetFacings(a.Theme),
            OriginalPens = CountPens(_levels[designName].Analysis, _surveys[designName]),
            OriginalSurvey = _surveys[designName]
        };
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

    /// <summary>
    /// Returns the walk from the start to the farthest point of the tour over the square root of the floor area.
    /// </summary>
    public static float GetElongation(RouteAnalysis a) => a.SpinePixels / (a.Step * MathF.Sqrt(Math.Max(1, a.ReachableCells)));

    private int GetHitPointTarget(int level) => (int)MathF.Round(_levels[GetCampaignName(level)].Measures.HitPoints * HitPointFactor);
}

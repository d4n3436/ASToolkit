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

    private readonly Dictionary<string, ReferenceLevel> _levels;
    private readonly Dictionary<LevelTheme, DecorHeightTable> _campaignDecor;
    private readonly Dictionary<LevelTheme, WallClearanceTable> _wallClearance;
    private readonly Dictionary<LevelTheme, FacingTable> _facings;

    private ReferenceLevelSet(Dictionary<string, ReferenceLevel> levels, Dictionary<LevelTheme, DecorHeightTable> campaignDecor,
        Dictionary<LevelTheme, WallClearanceTable> wallClearance, Dictionary<LevelTheme, FacingTable> facings, ObjectRelationTable relations)
    {
        _levels = levels;
        _campaignDecor = campaignDecor;
        _wallClearance = wallClearance;
        _facings = facings;
        Relations = relations;
    }

    /// <summary>
    /// Gets how the objects of the campaign levels, or of every level in add-on style, stand by each other, walls and doorways.
    /// </summary>
    public ObjectRelationTable Relations { get; }

    /// <summary>
    /// Gets the indoor campaign levels, 2 to 10, the ones with lab or military floor.
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
        var levels = new Dictionary<string, ReferenceLevel>();
        var campaignDecor = Themes.ToDictionary(theme => theme, _ => new DecorHeightTable());
        var campaign = CampaignLevels.Select(GetCampaignName).ToHashSet();
        string[] names = campaign
            .Concat(FirstLevels)
            .Concat(AddOn1)
            .Concat(AddOn2)
            .ToArray();
        
        var loader = new LevelLoader(objects);
        var loaded = new ReferenceLevel[names.Length];
        Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            string name = names[i];
            loaded[i] = loader.Load(name, Path.Combine(gameFolder, "Maps", name)) ?? throw new InvalidDataException($"{name} has no route.");
        });

        foreach (var level in loaded)
        {
            levels[level.Name] = level;
            if (addOnStyle || campaign.Contains(level.Name) || level.Name == FirstLevels[0])
            {
                foreach (var sprite in level.Map.Sprites.Where(s => s.Id.IsDecor))
                {
                    campaignDecor[level.Analysis.Theme].Add(sprite.Id, sprite.Position.Z);
                }
            }
        }

        var models = loaded.Select(level => level.Model).ToArray();
        var wallClearance = Themes
            .ToDictionary(t => t, t => WallClearanceTable.Build(models.Where(m => m.Theme == t)));
        var leaning = FacingTable.FindLeaningFrames(models, objects);
        var facings = Themes
            .ToDictionary(t => t, t => FacingTable.Build(models.Where(m => m.Theme == t), objects, leaning));
        var followed = loaded.Where(level => addOnStyle || campaign.Contains(level.Name));
        return new ReferenceLevelSet(levels, campaignDecor, wallClearance, facings, ObjectRelationTable.Build(followed, objects));
    }

    /// <summary>
    /// Returns a reference level.
    /// </summary>
    public ReferenceLevel GetLevel(string name) => _levels[name];

    /// <summary>
    /// Gets the names of the reference levels.
    /// </summary>
    public IReadOnlyCollection<string> LevelNames => _levels.Keys;

    /// <summary>
    /// Returns the survey of a reference level.
    /// </summary>
    public LevelSurvey GetSurvey(string name) => _levels[name].Survey;

    /// <summary>
    /// Returns the rooms of a reference level with their roles and types.
    /// </summary>
    public RoomAtlas GetAtlas(string name) => _levels[name].Atlas;

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
        var reference = _levels[GetCampaignName(level)];
        var (a, m) = (reference.Analysis, reference.Measures);
        string designName = contentLevel is not null && _levels.ContainsKey(contentLevel) ? contentLevel : GetCampaignName(level);
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
            Weapons = (contentLevel is not null && _levels.TryGetValue(contentLevel, out var weaponSource) ? weaponSource.Analysis : a).Elements.Count(e => e.Kind is ElementKind.Pickup or ElementKind.Crate && e.Category == ItemCategory.Weapon),
            ThemeDecor = GetThemeDecor(a.Theme),
            WallClearance = GetWallClearance(a.Theme),
            Facings = GetFacings(a.Theme),
            OriginalPens = CountPens(_levels[designName].Analysis, _levels[designName].Survey),
            OriginalSurvey = _levels[designName].Survey
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

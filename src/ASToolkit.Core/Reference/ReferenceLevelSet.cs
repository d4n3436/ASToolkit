using System.Collections.Frozen;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the game's own levels analyzed.
/// </summary>
public sealed class ReferenceLevelSet
{
    private static readonly string[] AddOn1 = ["ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map"];
    private static readonly string[] AddOn2 = ["ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"];
    private static readonly string[] FirstLevels = ["Level_01.map", "ADDON/Level_01.map", "ADDON2/Level_01.map"];

    private static readonly FrozenDictionary<string, Building> Buildings = new Dictionary<string, Building>
    {
        ["Level_01.map"] = Building.Grounds,
        ["Level_02.map"] = Building.UndergroundLab,
        ["Level_03.map"] = Building.UndergroundLab,
        ["Level_04.map"] = Building.UndergroundLab,
        ["Level_05.map"] = Building.UndergroundLab,
        ["Level_06.map"] = Building.Warehouse,
        ["Level_07.map"] = Building.Warehouse,
        ["Level_08.map"] = Building.Warehouse,
        ["Level_09.map"] = Building.Warehouse,
        ["Level_10.map"] = Building.Prison,
        ["ADDON/Level_01.map"] = Building.Grounds,
        ["ADDON/Level_02.map"] = Building.OfficeBlock,
        ["ADDON/Level_03.map"] = Building.ConferenceBuilding,
        ["ADDON/Level_04.map"] = Building.Warehouse,
        ["ADDON/Level_05.map"] = Building.Warehouse,
        ["ADDON2/Level_01.map"] = Building.Grounds,
        ["ADDON2/Level_02.map"] = Building.School,
        ["ADDON2/Level_03.map"] = Building.TrainingComplex,
        ["ADDON2/Level_04.map"] = Building.Warehouse,
        ["ADDON2/Level_05.map"] = Building.Warehouse
    }.ToFrozenDictionary();

    private readonly Dictionary<string, ReferenceLevel> _levels;

    private ReferenceLevelSet(Dictionary<string, ReferenceLevel> levels) => _levels = levels;

    /// <summary>
    /// Gets the indoor campaign levels, 2 to 10.
    /// </summary>
    public static IReadOnlyList<int> CampaignLevels { get; } = [2, 3, 4, 5, 6, 7, 8, 9, 10];

    /// <summary>
    /// Returns the building an original level is, as its mission text names it.
    /// </summary>
    public static Building GetBuilding(string name) => Buildings[name];

    /// <summary>
    /// Returns the floor set every original level of a building lies on.
    /// </summary>
    public static WallSet GetFloorSet(Building building) => building switch
    {
        Building.Grounds => WallSet.Outdoor,
        Building.UndergroundLab or Building.OfficeBlock or Building.ConferenceBuilding or Building.School or Building.TrainingComplex => WallSet.Basement,
        Building.Warehouse or Building.Prison => WallSet.Concrete,
        _ => throw new ArgumentOutOfRangeException(nameof(building), building, "Expected a known building.")
    };

    /// <summary>
    /// Returns the file name of a campaign level.
    /// </summary>
    public static string GetCampaignName(int level) => $"Level_{level:00}.map";

    /// <summary>
    /// Analyzes the campaign levels, the fighting levels of both add-ons and the three level 1 maps.
    /// </summary>
    public static ReferenceLevelSet Load(string gameFolder, ObjectTable objects)
    {
        var levels = new Dictionary<string, ReferenceLevel>();
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
        }

        return new ReferenceLevelSet(levels);
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
    /// Returns the walk from the start to the farthest point of the tour over the square root of the floor area.
    /// </summary>
    public static float GetElongation(RouteAnalysis a) => a.SpinePixels / (a.Step * MathF.Sqrt(Math.Max(1, a.ReachableCells)));

}

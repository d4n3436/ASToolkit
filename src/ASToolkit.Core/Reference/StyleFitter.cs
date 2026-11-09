using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the fitting of a style model to the original levels of a style, with the option of leaving some originals out to test how much
/// generation depends on each.
/// </summary>
public sealed class StyleFitter
{
    private static readonly WallSet[] WallSets = [WallSet.Basement, WallSet.Concrete, WallSet.Outdoor];

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private static readonly string ArenaLevel = ReferenceLevelSet.GetCampaignName(10);

    private readonly ObjectTable _objects;
    private readonly CampaignFitter _campaign;

    /// <summary>
    /// Initializes a new instance of the <see cref="StyleFitter"/> class.
    /// </summary>
    /// <param name="objects">The game's object table.</param>
    /// <param name="table">The game's shop and money tables.</param>
    public StyleFitter(ObjectTable objects, GameTable table)
    {
        _objects = objects;
        _campaign = new CampaignFitter(table, objects);
    }

    /// <summary>
    /// Returns the original levels a style is fitted to in order, the campaign's ten and in add-on style both add-ons' five after them.
    /// </summary>
    public static IReadOnlyList<string> GetFittingLevels(GenerationStyle style)
    {
        var campaign = Enumerable.Range(1, 10).Select(ReferenceLevelSet.GetCampaignName);
        return (style == GenerationStyle.AddOn ? campaign.Concat(AddOnLevels) : campaign).ToArray();
    }

    /// <summary>
    /// Returns the original levels a style's layout is fitted to, its fitting levels other than the campaign's arena level.
    /// </summary>
    public static IReadOnlyList<string> GetLayoutLevels(GenerationStyle style) => GetFittingLevels(style).Where(name => name != ArenaLevel).ToArray();

    /// <summary>
    /// Fits the model of a style to its original levels.
    /// </summary>
    public StyleModel Fit(ReferenceLevelSet references, GenerationStyle style) => Fit(references, style, []);

    /// <summary>
    /// Fits the model of a style to its original levels other than the excluded ones.
    /// </summary>
    public StyleModel Fit(ReferenceLevelSet references, GenerationStyle style, IReadOnlyCollection<string> excluded)
    {
        string[] fitting = GetFittingLevels(style)
            .Where(name => !excluded.Contains(name))
            .ToArray();

        var levels = references.LevelNames
            .Where(fitting.Contains)
            .Select(references.GetLevel)
            .ToArray();

        var models = levels
            .Select(level => level.Model)
            .ToArray();

        var leaning = FacingTable.FindLeaningFrames(models, _objects);
        var layoutLevels = levels.Where(level => GetLayoutLevels(style).Contains(level.Name)).ToArray();
        return new StyleModel(
            style,
            fitting,
            ObjectRelationTable.Build(levels, _objects),
            WallSets.ToDictionary(wallSet => wallSet, wallSet => WallClearanceTable.Build(models.Where(model => model.WallSet == wallSet).ToArray(), _objects)),
            WallSets.ToDictionary(wallSet => wallSet, wallSet => FacingTable.Build(models.Where(model => model.WallSet == wallSet), _objects, leaning)),
            _campaign.Fit(references, style, excluded))
        {
            Footprints = RoomFootprintFitter.Fit(layoutLevels),
            Encounters = EncounterModel.Fit(layoutLevels),
            Economy = EconomyModel.Fit(layoutLevels, GetLayoutLevels(GenerationStyle.AddOn).Where(name => !excluded.Contains(name)).Select(references.GetLevel).ToArray())
        };
    }
}

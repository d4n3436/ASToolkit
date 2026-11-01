using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the fitting of a style model to the original levels of a style.
/// </summary>
public sealed class StyleFitter
{
    private static readonly LevelTheme[] Themes = [LevelTheme.Lab, LevelTheme.Military, LevelTheme.Outdoor];

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private readonly ObjectTable _objects;

    /// <summary>
    /// Initializes a new instance of the <see cref="StyleFitter"/> class.
    /// </summary>
    public StyleFitter(ObjectTable objects) => _objects = objects;

    /// <summary>
    /// Returns the original levels a style is fitted to in order, the campaign's ten and in add-on style both add-ons' five after them.
    /// </summary>
    public static IReadOnlyList<string> GetFittingLevels(GenerationStyle style)
    {
        var campaign = Enumerable.Range(1, 10).Select(ReferenceLevelSet.GetCampaignName);
        return (style == GenerationStyle.AddOn ? campaign.Concat(AddOnLevels) : campaign).ToArray();
    }

    /// <summary>
    /// Fits the model of a style to its original levels.
    /// </summary>
    public StyleModel Fit(ReferenceLevelSet references, GenerationStyle style) => Fit(references, style, []);

    /// <summary>
    /// Fits the model of a style to its original levels other than the excluded ones.
    /// </summary>
    public StyleModel Fit(ReferenceLevelSet references, GenerationStyle style, IReadOnlyCollection<string> excluded)
    {
        var fitting = GetFittingLevels(style)
            .Where(name => !excluded.Contains(name))
            .ToArray();

        var levels = references.LevelNames
            .Where(fitting.Contains)
            .Select(references.GetLevel)
            .ToArray();

        var decorHeights = Themes.ToDictionary(theme => theme, _ => new DecorHeightTable());
        foreach (var level in levels)
        {
            foreach (var sprite in level.Map.Sprites.Where(sprite => sprite.Id.IsDecor))
            {
                decorHeights[level.Analysis.Theme].Add(sprite.Id, sprite.Position.Z);
            }
        }

        var models = levels
            .Select(level => level.Model)
            .ToArray();

        var leaning = FacingTable.FindLeaningFrames(models, _objects);
        return new StyleModel(
            style,
            fitting,
            ObjectRelationTable.Build(levels, _objects),
            decorHeights,
            Themes.ToDictionary(theme => theme, theme => WallClearanceTable.Build(models.Where(model => model.Theme == theme))),
            Themes.ToDictionary(theme => theme, theme => FacingTable.Build(models.Where(model => model.Theme == theme), _objects, leaning)));
    }
}

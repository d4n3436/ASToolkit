using System.Collections.Frozen;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents everything a generator takes from the original levels of a style, the campaign model and, per theme, the decor heights, wall
/// clearances, wall facings and object relations.
/// </summary>
public sealed class StyleModel
{
    private readonly FrozenDictionary<LevelTheme, DecorHeightTable> _decorHeights;
    private readonly FrozenDictionary<LevelTheme, WallClearanceTable> _wallClearance;
    private readonly FrozenDictionary<LevelTheme, FacingTable> _facings;

    /// <summary>
    /// Initializes a new instance of the <see cref="StyleModel"/> class.
    /// </summary>
    public StyleModel(GenerationStyle style, IReadOnlyList<string> fittingLevels, ObjectRelationTable relations, IReadOnlyDictionary<LevelTheme, DecorHeightTable> decorHeights,
        IReadOnlyDictionary<LevelTheme, WallClearanceTable> wallClearance, IReadOnlyDictionary<LevelTheme, FacingTable> facings, CampaignModel campaign)
    {
        Style = style;
        FittingLevels = fittingLevels;
        Relations = relations;
        _decorHeights = decorHeights.ToFrozenDictionary();
        _wallClearance = wallClearance.ToFrozenDictionary();
        _facings = facings.ToFrozenDictionary();
        Campaign = campaign;
    }

    /// <summary>
    /// Gets the style of the model.
    /// </summary>
    public GenerationStyle Style { get; }

    /// <summary>
    /// Gets the campaign model of the style.
    /// </summary>
    public CampaignModel Campaign { get; }

    /// <summary>
    /// Gets the original levels the model is fitted to, in order.
    /// </summary>
    public IReadOnlyList<string> FittingLevels { get; }

    /// <summary>
    /// Gets how the objects of the fitted levels stand by each other, walls and doorways.
    /// </summary>
    public ObjectRelationTable Relations { get; }

    /// <summary>
    /// Returns the heights the decor of a theme stands at.
    /// </summary>
    public DecorHeightTable GetDecorHeights(LevelTheme theme) => _decorHeights[theme];

    /// <summary>
    /// Returns how far the objects of a theme stand from walls.
    /// </summary>
    public WallClearanceTable GetWallClearance(LevelTheme theme) => _wallClearance[theme];

    /// <summary>
    /// Returns how the objects of a theme face the walls they stand against.
    /// </summary>
    public FacingTable GetFacings(LevelTheme theme) => _facings[theme];
}

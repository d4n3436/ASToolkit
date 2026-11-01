using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents what a generated level must reach, taken from the campaign level it replaces.
/// </summary>
public sealed class LevelTarget
{
    /// <summary>Gets the name of the original level whose pickups the generated level copies.</summary>
    public required string ContentLevel { get; init; }

    /// <summary>Gets the gating style of the original.</summary>
    public required LevelStyle Style { get; init; }

    /// <summary>Gets the total monster hit points aimed for.</summary>
    public required int HitPoints { get; init; }

    /// <summary>Gets the share of the level behind locked doors, or null.</summary>
    public float? LockedShare { get; init; }

    /// <summary>Gets the share of the level behind fences, or null.</summary>
    public float? FencedOffShare { get; init; }

    /// <summary>Gets the walk from the start to the breaker with the doors locked, or null.</summary>
    public int? WalkToBreakerPixels { get; init; }

    /// <summary>Gets a value indicating whether the level has a checkpoint.</summary>
    public required bool HasCheckpoint { get; init; }

    /// <summary>Gets the number of dynamite sites.</summary>
    public required int DynamiteSites { get; init; }

    /// <summary>Gets the number of secret closets.</summary>
    public required int SecretClosets { get; init; }

    /// <summary>Gets the number of weapons, loose and in crates.</summary>
    public required int Weapons { get; init; }

    /// <summary>Gets the decor the campaign uses on levels of the theme, with the heights it places each at.</summary>
    public required DecorHeightTable ThemeDecor { get; init; }

    /// <summary>Gets how close the originals of the theme place each decor object to a wall.</summary>
    public required WallClearanceTable WallClearance { get; init; }

    /// <summary>Gets the facings the originals of the theme give each decor object against walls.</summary>
    public required FacingTable Facings { get; init; }

    /// <summary>Gets the number of fenced pens holding spawners in the campaign original.</summary>
    public required int OriginalPens { get; init; }

    /// <summary>Gets the places, encounters and items of the campaign original.</summary>
    public required LevelSurvey OriginalSurvey { get; init; }

}

using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents what a generated level must reach, taken from the campaign level it replaces.
/// </summary>
public sealed class LevelTarget
{
    /// <summary>Gets the name of the original level whose pickups the generated level copies.</summary>
    public required string ContentLevel { get; init; }

    /// <summary>Gets the campaign level number.</summary>
    public required int Level { get; init; }

    /// <summary>Gets the gating style of the original.</summary>
    public required LevelStyle Style { get; init; }

    /// <summary>Gets the share of the floor that solid objects block.</summary>
    public required float BlockedShare { get; init; }

    /// <summary>Gets the number of breakable objects.</summary>
    public required int Breakables { get; init; }

    /// <summary>Gets the tour length of the campaign and add-on mix.</summary>
    public required int ReferenceTourPixels { get; init; }

    /// <summary>Gets the highest share of the tour that may walk over covered ground.</summary>
    public required float RevisitCeiling { get; init; }

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

    /// <summary>Gets a value indicating whether the level has a boss.</summary>
    public required bool HasBoss { get; init; }

    /// <summary>Gets a value indicating whether the level has a mounted cannon.</summary>
    public required bool HasMountedCannon { get; init; }

    /// <summary>Gets the chance that a monster group comes from an ambush rather than being placed.</summary>
    public required float AmbushChance { get; init; }

    /// <summary>Gets the median tour progress of the placed monsters, or null when there are fewer than four.</summary>
    public float? PlacedMonsterProgress { get; init; }

    /// <summary>Gets the number of weapons, loose and in crates.</summary>
    public required int Weapons { get; init; }

    /// <summary>Gets the number of fence generators.</summary>
    public required int Generators { get; init; }

    /// <summary>Gets a value indicating whether an ambush triggers right at a generator.</summary>
    public required bool HasGeneratorAmbush { get; init; }

    /// <summary>Gets the length in wall pieces of the wall the original's breaker hangs on, or 0 without a breaker.</summary>
    public required int BreakerWallRun { get; init; }

    /// <summary>Gets the share of the floor's bounding box that is floor.</summary>
    public required float BoxFill { get; init; }

    /// <summary>Gets the blended share of the tour that the walk from the start to its farthest point takes.</summary>
    public required float SpineShare { get; init; }

    /// <summary>Gets the share of the tour that the walk from the start to its farthest point takes in the campaign original.</summary>
    public required float OriginalSpineShare { get; init; }

    /// <summary>Gets the blended walk from the start to the farthest point of the tour, over the square root of the floor area.</summary>
    public required float Elongation { get; init; }

    /// <summary>Gets the walk from the start to the farthest point of the tour, over the square root of the floor area, in the campaign original.</summary>
    public required float OriginalElongation { get; init; }

    /// <summary>Gets the decor the campaign uses on levels of the theme, with the heights it places each at.</summary>
    public required DecorHeightTable ThemeDecor { get; init; }

    /// <summary>Gets how close the originals of the theme place each decor object to a wall.</summary>
    public required WallClearanceTable WallClearance { get; init; }

    /// <summary>Gets the facings the originals of the theme give each decor object against walls.</summary>
    public required FacingTable Facings { get; init; }
}

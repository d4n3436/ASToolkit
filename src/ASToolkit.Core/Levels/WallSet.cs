namespace ASToolkit.Core.Levels;

/// <summary>
/// Specifies the tile sets a level is built from, deciding its lattice, floor and wall pieces, and whether it closes with doors or energy fences.
/// </summary>
public enum WallSet
{
    /// <summary>The basement floor (500, 502) with walls 1 and 2 (601 to 626) and their doors.</summary>
    Basement,

    /// <summary>The military base floor (503) with the concrete walls, corner posts and energy fences (650 to 654).</summary>
    Concrete,

    /// <summary>The grass and fences of the outdoor grounds.</summary>
    Outdoor
}

namespace ASToolkit.Core.Generation.Outdoor;

/// <summary>
/// Specifies which level 1 an outdoor level follows.
/// </summary>
public enum OutdoorStyle
{
    /// <summary>The campaign's walk to the base, won at a point inside it.</summary>
    Campaign,

    /// <summary>Add-on 1's compound, won at a point behind its only dynamite site.</summary>
    Compound,

    /// <summary>Add-on 2's city block, won by killing every monster.</summary>
    City
}

using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents how often a drawn building stands among a style's original levels after the first and where its levels lie in their campaigns,
/// such as warehouses on half the levels, late in the campaigns.
/// </summary>
/// <param name="Building">The building.</param>
/// <param name="Share">The share of the original levels after the first that are the building, among the drawn buildings.</param>
/// <param name="Position">The mean place of the building's levels in their campaigns, from 0 at the second level to 1 at the last.</param>
/// <param name="Samples">The number of original levels that are the building.</param>
/// <param name="IsRequired">Whether every generated campaign holds at least one level of the building.</param>
public sealed record BuildingShare(Building Building, float Share, float Position, int Samples, bool IsRequired);

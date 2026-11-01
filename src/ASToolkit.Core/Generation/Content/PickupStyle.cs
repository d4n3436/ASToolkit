using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents how an original level lays the pickups of one category.
/// </summary>
/// <param name="CrateShare">The share of the category's pickups in crates.</param>
/// <param name="PileSizes">The size of each loose pile of the category.</param>
/// <param name="PileSpots">Where each loose pile of the category lies.</param>
public sealed record PickupStyle(float CrateShare, IReadOnlyList<int> PileSizes, IReadOnlyList<PartSpot> PileSpots);

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the share of rooms of one size class whose outline is cut, with the weight of each cut outline class.
/// </summary>
/// <param name="Share">The share of the rooms with a cut outline.</param>
/// <param name="Classes">The weight of each cut outline class.</param>
public sealed record CutShare(float Share, IReadOnlyDictionary<OutlineClass, float> Classes);

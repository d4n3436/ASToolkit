using ASToolkit.Formats;

namespace ASToolkit.Analysis.Reference;

/// <summary>
/// Represents a kind of monster in a style's campaigns.
/// </summary>
/// <param name="Id">The monster kind.</param>
/// <param name="FirstLevel">The level the kind first appears on.</param>
/// <param name="HitPoints">The hit points of one monster of the kind.</param>
public readonly record struct MonsterKind(KnownObject Id, int FirstLevel, int HitPoints);

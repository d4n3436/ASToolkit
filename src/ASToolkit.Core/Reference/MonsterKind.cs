using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents a monster kind of a style, the level it first appears on and its hit points.
/// </summary>
/// <param name="Id">The monster kind.</param>
/// <param name="FirstLevel">The level the kind first appears on.</param>
/// <param name="HitPoints">The hit points of one monster of the kind.</param>
public readonly record struct MonsterKind(KnownObject Id, int FirstLevel, int HitPoints);

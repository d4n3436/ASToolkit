namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents a fight placed in a generated level.
/// </summary>
/// <param name="Progress">The progress along the walk where the fight starts.</param>
/// <param name="HitPoints">The monsters' hit points.</param>
/// <param name="Region">The plan region the fight happens in.</param>
public sealed record PlacedFight(float Progress, int HitPoints, int Region);

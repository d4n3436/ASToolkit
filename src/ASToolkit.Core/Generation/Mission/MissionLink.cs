namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Represents a link between two rooms of a mission graph.
/// </summary>
/// <param name="From">The room the link is walked from.</param>
/// <param name="To">The room the link leads into.</param>
/// <param name="IsLocked">A value indicating whether the link is a lock that a key opens.</param>
/// <param name="Kind">The part the link plays in the mission graph.</param>
public readonly record struct MissionLink(int From, int To, bool IsLocked, MissionLinkKind Kind);

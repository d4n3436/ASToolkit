namespace ASToolkit.Core.Generation.Campaign;

/// <summary>
/// Represents a generated campaign drawn from one seed as the brief of each of its levels, from level 1 to the last.
/// </summary>
public sealed record CampaignPlan
{
    /// <summary>
    /// Gets the brief of each level from level 1.
    /// </summary>
    public required IReadOnlyList<LevelBrief> Levels { get; init; }
}

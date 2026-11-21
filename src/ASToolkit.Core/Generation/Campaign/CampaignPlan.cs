namespace ASToolkit.Core.Generation.Campaign;

/// <summary>
/// Represents a generated campaign, which is the brief of each of its levels from level 1 to the last, drawn from one seed.
/// </summary>
public sealed record CampaignPlan
{
    /// <summary>
    /// Gets the brief of each level from level 1.
    /// </summary>
    public required IReadOnlyList<LevelBrief> Levels { get; init; }
}

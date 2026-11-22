namespace ASToolkit.Core.Bot;

/// <summary>
/// Represents the results and remaining equipment of an ordered campaign run.
/// </summary>
/// <param name="Levels">The results of the levels attempted in campaign order.</param>
/// <param name="State">The money and equipment retained after the last completed level.</param>
/// <param name="IsCompleted">Whether every supplied level was completed.</param>
public sealed record CampaignResult(IReadOnlyList<PlaythroughResult> Levels, CampaignState State, bool IsCompleted);

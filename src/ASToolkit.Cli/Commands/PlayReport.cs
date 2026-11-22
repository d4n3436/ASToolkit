using ASToolkit.Bot;

namespace ASToolkit.Cli.Commands;

internal sealed record PlayReport(string Campaign, int FirstLevel, CampaignState InitialState, double PreparationMilliseconds,
    double PlayMilliseconds, IReadOnlyList<LevelReport> Levels, CampaignState FinalState, bool IsCompleted);

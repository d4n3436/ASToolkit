using ASToolkit.Core.Routes;
using ASToolkit.Core.Bot;

namespace ASToolkit.Cli.Commands;

internal sealed record PlayReport(string Campaign, int FirstLevel, CampaignState InitialState, double PreparationMilliseconds,
    double PlayMilliseconds, CampaignResult Result);

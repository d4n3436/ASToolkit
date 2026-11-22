using ASToolkit.Core.Routes;
using ASToolkit.Core.Simulation;

namespace ASToolkit.Cli.Commands;

internal sealed record SimulationReport(string Campaign, int FirstLevel, CampaignState InitialState, double PreparationMilliseconds,
    double SimulationMilliseconds, CampaignResult Result);

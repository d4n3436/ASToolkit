using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Simulation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class SimulateCommand : Command<SimulateSettings>
{
    private static readonly Campaign[] Campaigns =
    [
        new("original", "", 10, "LevelMoney"),
        new("addon", "ADDON", 5, "LevelMoneyAddon"),
        new("addon2", "ADDON2", 5, "LevelMoneyAddon2")
    ];

    public override int Execute(CommandContext context, SimulateSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        var table = GameTable.Load(Path.Combine(settings.Game, "Maps", "common_table.lgc"));
        var reports = new List<Report>();
        var output = new Table().AddColumns("Campaign", "Completed levels", "Preparation", "Simulation", "Outcome");
        foreach (var campaign in Campaigns.Where(campaign => settings.Campaign == "all" || campaign.Name == settings.Campaign))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            var levels = new List<RouteAnalysis>();
            for (int level = settings.Start; level <= campaign.LastLevel; level++)
            {
                string name = Path.Combine(campaign.Folder, $"Level_{level:00}.map");
                string path = Path.Combine(settings.Game, "Maps", name);
                var map = MapResourceBuilder.Load(path);
                levels.Add(RouteAnalyzer.Analyze(name, map, LevelScriptReader.Read(Path.ChangeExtension(path, ".lgc")), objects)
                    ?? throw new InvalidDataException($"{name} has no route."));
            }

            var rewards = table.GetArray(campaign.Rewards);
            int cash = settings.Money ?? CampaignState.StartingMoney + (settings.Start > 1 ? rewards[settings.Start - 1] : 0);
            var initial = CampaignState.CreateInitial(cash);
            double preparation = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var result = new Playthrough(settings.Game, objects, table).PlayCampaign(levels, rewards.Skip(settings.Start).ToArray(), settings.Seed, initial);
            double simulation = clock.Elapsed.TotalMilliseconds;
            reports.Add(new Report(campaign.Name, settings.Start, initial, preparation, simulation, result));
            output.AddRow(campaign.Name, $"{result.Levels.Count(level => level.Survived)}/{levels.Count}", $"{preparation / 1000:F3}s",
                $"{simulation / 1000:F3}s", result.IsCompleted ? "completed" : $"stopped at level {settings.Start + result.Levels.Count - 1}");
        }

        AnsiConsole.Write(output);

        if (settings.Out is not null)
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            File.WriteAllText(settings.Out, JsonSerializer.Serialize(reports, options));
        }

        return reports.All(report => report.Result.IsCompleted) ? 0 : 1;
    }

    private sealed record Campaign(string Name, string Folder, int LastLevel, string Rewards);
    
    private sealed record Report(string Campaign, int FirstLevel, CampaignState InitialState, double PreparationMilliseconds,
        double SimulationMilliseconds, CampaignResult Result);
}

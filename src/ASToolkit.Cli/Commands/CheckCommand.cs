using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class CheckCommand : Command<CheckSettings>
{
    public override int Execute(CommandContext context, CheckSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        var references = AnsiConsole.Status().Start("Analyzing the original levels", _ => ReferenceLevelSet.Load(settings.Game, objects));
        string folder = settings.Levels ?? Path.Combine(settings.Game, "Maps");
        var table = new Table().AddColumns("Level", "Hard rules broken", "Soft rules met");
        var details = new List<string>();
        int rejected = 0;
        
        var levels = ReferenceLevelSet.CampaignLevels;
        var evaluated = new RuleResult[levels.Count][];
        bool[] routed = new bool[levels.Count];
        bool[] found = new bool[levels.Count];
        Parallel.For(0, levels.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            string mapPath = Path.Combine(folder, ReferenceLevelSet.GetCampaignName(levels[i]));
            found[i] = File.Exists(mapPath);
            if (!found[i])
            {
                return;
            }

            var map = GameMap.Load(mapPath);
            var script = LevelScriptReader.Read(Path.ChangeExtension(mapPath, ".lgc"));
            var analysis = RouteAnalyzer.Analyze(ReferenceLevelSet.GetCampaignName(levels[i]), map, script, objects);
            routed[i] = analysis is not null;
            evaluated[i] = analysis is null ? [] : RuleSet.Default.Evaluate(RuleContext.Create(map, script, analysis, objects, references.GetTarget(levels[i])));
        });

        for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
        {
            string name = ReferenceLevelSet.GetCampaignName(levels[levelIndex]);
            if (!found[levelIndex])
            {
                table.AddRow(name, "[grey]missing[/]", "");
                continue;
            }

            var results = evaluated[levelIndex];
            if (!routed[levelIndex])
            {
                table.AddRow(name, "[red]no route[/]", "");
                rejected++;
                continue;
            }

            var hard = results.Where(r => r is { Severity: RuleSeverity.Hard, Passed: false }).ToArray();
            var soft = results.Where(r => r.Severity == RuleSeverity.Soft).ToArray();
            if (hard.Length > 0)
            {
                rejected++;
            }

            table.AddRow(name,
                hard.Length == 0 ? "[green]none[/]" : $"[red]{Markup.Escape(string.Join(", ", hard.Select(r => r.Id).Distinct()))}[/]",
                $"{soft.Count(r => r.Passed)} of {soft.Length}");
            
            foreach (var result in results.Where(r => settings.Verbose || !r.Passed))
            {
                string mark = result.Passed ? "[green]ok[/]  " : result.Severity == RuleSeverity.Hard ? "[red]HARD[/]" : "[yellow]soft[/]";
                details.Add($"{name} {mark} {result.Id,-5} {Markup.Escape(result.Detail)}");
            }
        }

        AnsiConsole.Write(table);
        foreach (string line in details)
        {
            AnsiConsole.MarkupLine(line);
        }

        return rejected == 0 ? 0 : 1;
    }
}

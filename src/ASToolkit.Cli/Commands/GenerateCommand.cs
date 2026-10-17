using ASToolkit.Core.Generation;
using ASToolkit.Core.Rules;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class GenerateCommand : Command<GenerateSettings>
{
    public override int Execute(CommandContext context, GenerateSettings settings, CancellationToken cancellationToken)
    {
        var (generator, _, _) = GeneratorFactory.Create(settings.Game, settings.AddOnStyle);
        var table = new Table().AddColumns("Level", "Gate", "Hard rules broken", "Soft rules met", "Roof misses");
        int broken = 0;
        int[] levels = settings.AddOnStyle
            ? Enumerable.Range(1, LevelGenerator.AddOnLevelCount).ToArray()
            : Enumerable.Range(settings.First, settings.Last - settings.First + 1).ToArray();
        var generatedLevels = new GeneratedLevel[levels.Length];
        int done = 0;
        AnsiConsole.Status().Start($"Generating {levels.Length} levels", status =>
        {
            var options = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.For(0, levels.Length, options, i =>
            {
                generatedLevels[i] = settings.AddOnStyle ? generator.GenerateAddOn(settings.Seed, levels[i]) : generator.Generate(settings.Seed, levels[i]);
                status.Status($"Generated {Interlocked.Increment(ref done)} of {levels.Length} levels");
            });
        });

        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            int level = levels[levelIndex];
            var generated = generatedLevels[levelIndex];
            var hard = generated.Results.Where(r => r is { Severity: RuleSeverity.Hard, Passed: false }).ToArray();
            var soft = generated.Results.Where(r => r.Severity == RuleSeverity.Soft).ToArray();
            if (hard.Length > 0)
            {
                broken++;
            }

            table.AddRow($"{RandomAddOnInstaller.FolderName}/Level_{level:00}.map", generated.Plan.Gate?.ToString() ?? "none",
                hard.Length == 0 ? "[green]none[/]" : $"[red]{Markup.Escape(string.Join(", ", hard.Select(r => r.Id).Distinct()))}[/]",
                $"{soft.Count(r => r.Passed)} of {soft.Length}", $"{generated.RoofResidual} px");
        }

        RandomAddOnInstaller.Install(settings.Game, settings.AddOnStyle, settings.Seed,
            levels.Select((n, i) => new RandomAddOnLevel(n, generatedLevels[i], settings.AddOnStyle ? generator.GetAddOnTemplatePath(n) : generator.GetTemplatePath(n))).ToArray());
        AnsiConsole.Write(table);
        if (broken > 0)
        {
            AnsiConsole.MarkupLine($"[red]{broken} level(s) break a hard rule after every candidate; they were installed anyway.[/]");
            return 1;
        }

        return 0;
    }
}

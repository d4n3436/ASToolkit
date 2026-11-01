using ASToolkit.Core.Generation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class GenerateCommand : Command<GenerateSettings>
{
    public override int Execute(CommandContext context, GenerateSettings settings, CancellationToken cancellationToken)
    {
        var (generator, _, _) = GeneratorFactory.Create(settings.Game, settings.AddOnStyle);
        var table = new Table().AddColumns("Level", "Gate", "Tour", "Walked back", "Unreached");
        int broken = 0;
        int[] levels = settings.AddOnStyle
            ? Enumerable.Range(1, LevelGenerator.AddOnLevelCount).ToArray()
            : Enumerable.Range(settings.First, settings.Last - settings.First + 1).ToArray();
        var generatedLevels = new GeneratedLevel[levels.Length];
        int done = 0;
        AnsiConsole.Status().Start($"Generating {levels.Length} levels", status =>
        {
            var options = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
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
            int unreached = generated.Analysis?.FallbackUsage.Unreached ?? 0;
            if (unreached > 0)
            {
                broken++;
            }

            table.AddRow($"{RandomAddOnInstaller.FolderName}/Level_{level:00}.map", generated.Plan?.Gate?.ToString() ?? "none",
                $"{generated.Analysis?.RoutePixels ?? 0} px", $"{generated.Analysis?.RevisitShare ?? 0:P0}",
                unreached == 0 ? "[green]none[/]" : $"[red]{unreached}[/]");
        }

        RandomAddOnInstaller.Install(settings.Game, settings.AddOnStyle, settings.Seed,
            levels.Select((n, i) => new RandomAddOnLevel(n, generatedLevels[i], generator.GetTemplatePath(generatedLevels[i]))).ToArray());
        AnsiConsole.Write(table);
        if (broken > 0)
        {
            AnsiConsole.MarkupLine($"[red]{broken} level(s) leave objectives out of reach on every layout; they were installed anyway.[/]");
            return 1;
        }

        return 0;
    }
}

using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class PreviewCommand : Command<PreviewSettings>
{
    public override int Execute(CommandContext context, PreviewSettings settings, CancellationToken cancellationToken)
    {
        var (generator, _, _) = GeneratorFactory.Create(settings.Game, settings.Style);
        var level = generator.Generate(settings.Seed, settings.Level);
        level.Write(settings.Out, generator.GetTemplatePath(level));
        string layout = level is { Plan: not null, Budget: not null }
            ? $"{level.Plan.Stations.Count} stations, {level.Plan.Regions.Count} regions, gate {level.Plan.Gate}, HP {level.Budget.PlacedHitPoints + level.Budget.AmbushHitPoints} of {level.Budget.TargetHitPoints}, "
            : "outdoor, ";
        AnsiConsole.MarkupLine($"Wrote [green]{Markup.Escape(settings.Out)}[/]: {layout}{level.Map.Sprites.Count} objects.");
        return 0;
    }
}

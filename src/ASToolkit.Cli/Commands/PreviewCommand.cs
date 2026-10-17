using ASToolkit.Core.Rules;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class PreviewCommand : Command<PreviewSettings>
{
    public override int Execute(CommandContext context, PreviewSettings settings, CancellationToken cancellationToken)
    {
        var (generator, _, _) = GeneratorFactory.Create(settings.Game, settings.AddOnStyle);
        var level = settings.AddOnStyle ? generator.GenerateAddOn(settings.Seed, settings.Level) : generator.Generate(settings.Seed, settings.Level);
        level.Write(settings.Out, settings.AddOnStyle ? generator.GetAddOnTemplatePath(settings.Level) : generator.GetTemplatePath(settings.Level));
        AnsiConsole.MarkupLine($"Wrote [green]{Markup.Escape(settings.Out)}[/]: {level.Plan.Stations.Count} stations, {level.Plan.Regions.Count} regions, {level.Map.Sprites.Count} objects, gate {level.Plan.Gate}, HP {level.Budget.PlacedHitPoints + level.Budget.AmbushHitPoints} of {level.Budget.TargetHitPoints}, roof misses {level.RoofResidual} px.");
        foreach (var result in level.Results.Where(r => !r.Passed))
        {
            AnsiConsole.MarkupLine($"{(result.Severity == RuleSeverity.Hard ? "[red]HARD[/]" : "[yellow]soft[/]")} {result.Id,-5} {Markup.Escape(result.Detail)}");
        }

        return 0;
    }
}

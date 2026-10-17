using ASToolkit.Core.Widescreen;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class WidescreenApplyCommand : Command<GameSettings>
{
    public override int Execute(CommandContext context, GameSettings settings, CancellationToken cancellationToken)
    {
        WidescreenInstaller.Install(settings.Game);
        AnsiConsole.MarkupLine("Widescreen mod [green]applied[/].");
        return 0;
    }
}

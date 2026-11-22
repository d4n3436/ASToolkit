using ASToolkit.Generation.Widescreen;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal sealed class WidescreenApplyCommand : Command<GameSettings>
{
    public override int Execute(GameSettings settings, CancellationToken cancellationToken)
    {
        WidescreenInstaller.Install(settings.Game);
        AnsiConsole.MarkupLine("Widescreen mod [green]applied[/].");
        return 0;
    }
}

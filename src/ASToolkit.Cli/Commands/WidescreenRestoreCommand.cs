using ASToolkit.Core.Widescreen;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class WidescreenRestoreCommand : Command<GameSettings>
{
    public override int Execute(CommandContext context, GameSettings settings, CancellationToken cancellationToken)
    {
        foreach (string file in WidescreenInstaller.Remove(settings.Game))
        {
            AnsiConsole.MarkupLine($"Restored [green]{Markup.Escape(file)}[/]");
        }

        return 0;
    }
}

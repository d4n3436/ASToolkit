using ASToolkit.Generation.Widescreen;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal sealed class WidescreenRestoreCommand : Command<GameSettings>
{
    public override int Execute(GameSettings settings, CancellationToken cancellationToken)
    {
        foreach (string file in WidescreenInstaller.Remove(settings.Game))
        {
            AnsiConsole.MarkupLine($"Restored [green]{Markup.Escape(file)}[/]");
        }

        return 0;
    }
}

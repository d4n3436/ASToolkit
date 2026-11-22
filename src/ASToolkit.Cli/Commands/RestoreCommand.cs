using Spectre.Console;
using ASToolkit.Generation.Patching;

namespace ASToolkit.Cli.Commands;

internal sealed class RestoreCommand : Command<GameSettings>
{
    public override int Execute(GameSettings settings, CancellationToken cancellationToken)
    {
        foreach (string part in RandomAddOnInstaller.Remove(settings.Game))
        {
            AnsiConsole.MarkupLine($"Removed [green]{Markup.Escape(part)}[/]");
        }

        return 0;
    }
}

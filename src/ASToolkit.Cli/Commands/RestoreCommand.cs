using ASToolkit.Core.Generation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class RestoreCommand : Command<GameSettings>
{
    public override int Execute(CommandContext context, GameSettings settings, CancellationToken cancellationToken)
    {
        foreach (string part in RandomAddOnInstaller.Remove(settings.Game))
        {
            AnsiConsole.MarkupLine($"Removed [green]{Markup.Escape(part)}[/]");
        }

        return 0;
    }
}

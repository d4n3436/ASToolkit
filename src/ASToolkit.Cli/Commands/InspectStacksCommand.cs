using System.Globalization;
using ASToolkit.Core.Scripting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectStacksCommand : Command<SpriteSelectionSettings>
{
    public override int Execute(CommandContext context, SpriteSelectionSettings settings, CancellationToken cancellationToken)
    {
        var selector = new SpriteSelector(settings);
        foreach ((var sprite, string objectName, var program) in selector.Select())
        {
            if (program is null || program.Value.StackCount == 0)
            {
                continue;
            }

            string name = program.Value.GetName();
            AnsiConsole.MarkupLine($"[bold]{sprite.Handle}[/] {(int)sprite.Id} {Markup.Escape(objectName)} at ({sprite.Position.X:0}, {sprite.Position.Y:0}){(name.Length > 0 ? " " + Markup.Escape(name) : "")}");
            for (int stack = 0; stack < program.Value.StackCount; stack++)
            {
                var entries = program.Value.GetStack(stack);
                AnsiConsole.MarkupLine($"  stack {stack}, {entries.Length} entries, run from the last");
                for (int entry = entries.Length - 1; entry >= 0; entry--)
                {
                    var action = entries[entry];
                    string act = Enum.IsDefined((ActionCode)action.Act) ? ((ActionCode)action.Act).ToString() : "?";
                    AnsiConsole.MarkupLine($"    {entry.ToString(CultureInfo.InvariantCulture),3} {action.Act,4} {act,-16} {action.Var1} {action.Var2} {action.Var3}");
                }
            }
        }

        return 0;
    }
}

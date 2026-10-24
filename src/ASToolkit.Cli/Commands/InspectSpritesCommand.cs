using System.Globalization;
using ASToolkit.Core.Scripting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectSpritesCommand : Command<SpriteSelectionSettings>
{
    public override int Execute(CommandContext context, SpriteSelectionSettings settings, CancellationToken cancellationToken)
    {
        var selector = new SpriteSelector(settings);
        var logic = selector.Objects.IsAlienShooter2
            ? LevelLogicReader.Read(selector.MapPath, selector.Map, selector.Objects, QuestBook.Load(settings.Game))
            : null;
        var table = new Table().AddColumns("Handle", "Object", "Name", "Position", "Army", "Facing", "Sprite name", "Opened by the scripts");
        foreach ((var sprite, string objectName, var program) in selector.Select())
        {
            string opening = "";
            if (logic is not null && logic.Openings.TryGetValue(sprite.Handle, out int timeLine))
            {
                opening = $"from timeline {timeLine}";
            }

            var trigger = logic?.Triggers.FirstOrDefault(candidate => candidate.Handles.Contains(sprite.Handle));
            if (trigger is not null)
            {
                opening += (opening.Length > 0 ? ", " : "") + $"by the {(trigger.IsProximity ? "area" : "trigger")} at ({trigger.Position.X:0}, {trigger.Position.Y:0}) radius {trigger.Radius:0}";
            }

            table.AddRow(sprite.Handle.ToString(CultureInfo.InvariantCulture), ((int)sprite.Id).ToString(CultureInfo.InvariantCulture), Markup.Escape(objectName),
                $"({sprite.Position.X:0}, {sprite.Position.Y:0}, {sprite.Position.Z:0})", sprite.Army.ToString(CultureInfo.InvariantCulture),
                sprite.Facing.ToString(CultureInfo.InvariantCulture), Markup.Escape(program?.GetName() ?? ""), Markup.Escape(opening));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

using ASToolkit.Core.Formats;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectObjectCommand : Command<InspectObjectSettings>
{
    private const int MaxLinkDepth = 8;

    public override int Execute(CommandContext context, InspectObjectSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        var id = (KnownObject)settings.Id;
        if (!objects.TryFind(id, out var obj))
        {
            AnsiConsole.MarkupLine($"[red]No object {settings.Id}.[/]");
            return 1;
        }

        var table = new Table().HideHeaders().AddColumns("Field", "Value");
        table.AddRow("id", $"{settings.Id} {Markup.Escape(objects.GetName(id))}");
        table.AddRow("file", Markup.Escape(objects.GetPath(id)));
        table.AddRow("type", obj.SpriteType.ToString());
        table.AddRow("class", $"{(int)obj.SpriteClass} {obj.SpriteClass}");
        table.AddRow("flags", obj.Flags.ToString());
        table.AddRow("move mask", $"0x{obj.MoveMask:X}");
        table.AddRow("size", $"{obj.Size.X} x {obj.Size.Y} x {obj.Size.Z}");
        table.AddRow("scale", $"{obj.Scale.X} x {obj.Scale.Y} x {obj.Scale.Z}");
        table.AddRow("hit points", obj.MaxHp.ToString());
        table.AddRow("climb, drop", $"{obj.StepHeight}, {obj.DropHeight}");
        table.AddRow("directions", obj.Directions.ToString());
        table.AddRow("gamma", $"{obj.GammaRed} {obj.GammaGreen} {obj.GammaBlue} {obj.GammaAlpha}");
        for (int state = 0; state < StateValues.Count; state++)
        {
            if (obj.FrameCounts[state] != 0 || obj.FrameDurations[state] != 0)
            {
                table.AddRow($"state {state} {(SpriteAnimation)state}", $"{obj.FrameCounts[state]} frames, duration {obj.FrameDurations[state]}");
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[bold]link chain[/]");
        var current = obj;
        var currentId = id;
        bool blocks = false;
        objects.TryFind(KnownObject.SequelPlayer, out var player);
        for (int depth = 0; depth < MaxLinkDepth; depth++)
        {
            bool blocksHere = current.IsSolid || (objects.IsAlienShooter2 && current.Blocks(player));
            blocks |= blocksHere;
            AnsiConsole.MarkupLine($"  {(int)currentId} {Markup.Escape(objects.GetName(currentId))}{(blocksHere ? " [yellow]blocks[/]" : "")}");
            if (current.Link == 0 || !objects.TryFind(current.Link, out var next))
            {
                break;
            }

            AnsiConsole.MarkupLine($"    offset ({current.LinkOffset.X}, {current.LinkOffset.Y}, {current.LinkOffset.Z})");
            currentId = current.Link;
            current = next;
        }

        AnsiConsole.MarkupLine($"blocks the player: {(blocks ? "[yellow]yes[/]" : "no")}");
        return 0;
    }
}

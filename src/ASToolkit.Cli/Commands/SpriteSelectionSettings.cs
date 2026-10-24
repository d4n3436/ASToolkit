using System.ComponentModel;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal class SpriteSelectionSettings : GameSettings
{
    [CommandArgument(0, "<MAP>")]
    [Description("The map to read, relative to the Maps folder or a path to a map file.")]
    public string Map { get; init; } = "";

    [CommandOption("--id <ID>")]
    [Description("Keeps the sprites of one object ID.")]
    public int? Id { get; init; }

    [CommandOption("--name <TEXT>")]
    [Description("Keeps the sprites whose object name or sprite name contains the text.")]
    public string? Name { get; init; }

    [CommandOption("--near <X,Y,R>")]
    [Description("Keeps the sprites within a radius of a point, in map pixels.")]
    public MapArea? Near { get; init; }
}

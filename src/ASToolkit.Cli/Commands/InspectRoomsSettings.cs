using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectRoomsSettings : GameSettings
{
    [CommandArgument(0, "[MAPS]")]
    [Description("The original maps to read, relative to the Maps folder, such as Level_03.map or ADDON/Level_02.map. Defaults to campaign levels 2 to 10 and the levels 2 to 5 of both add-ons.")]
    public string[] Maps { get; init; } = [];

    [CommandOption("-o|--out <FOLDER>", true)]
    [Description("The folder for the room sheets, rooms.tsv and rooms.md. It is created when missing.")]
    public string Out { get; init; } = "";

    [CommandOption("-l|--levels <FOLDER>")]
    [Description("A folder of generated maps and their .lgc scripts to read instead of the originals.")]
    public string? Levels { get; init; }

    [CommandOption("-s|--scale <SCALE>")]
    [Description("Image pixels per map pixel of the room crops, above 0 and at most 1.")]
    [DefaultValue(0.5f)]
    public float Scale { get; init; } = 0.5f;

    [CommandOption("--crops")]
    [Description("Also writes the image of each room to its own file in a crops folder.")]
    public bool Crops { get; init; }

    public override ValidationResult Validate()
    {
        var game = base.Validate();
        if (!game.Successful)
        {
            return game;
        }

        if (Levels is not null && !Directory.Exists(Levels))
        {
            return ValidationResult.Error($"No folder {Levels}.");
        }

        return Scale is > 0 and <= 1 ? ValidationResult.Success() : ValidationResult.Error("The scale must be above 0 and at most 1.");
    }
}

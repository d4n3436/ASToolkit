using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectDesignSettings : GameSettings
{
    [CommandOption("-o|--out <FOLDER>", true)]
    [Description("The folder for design.tsv, design.md and the generated levels. It is created when missing.")]
    public string Out { get; init; } = "";

    [CommandOption("--seeds <COUNT>")]
    [Description("How many seeds of campaign levels 2 to 10 to generate and measure against the originals. 0 measures the originals only.")]
    [DefaultValue(10)]
    public int Seeds { get; init; } = 10;

    [CommandOption("--images")]
    [Description("Writes an image of the places of every measured level.")]
    public bool Images { get; init; }

    [CommandOption("-s|--scale <SCALE>")]
    [Description("Image pixels per map pixel of the place images, above 0 and at most 1.")]
    [DefaultValue(0.15f)]
    public float Scale { get; init; } = 0.15f;

    public override ValidationResult Validate()
    {
        var game = base.Validate();
        if (!game.Successful)
        {
            return game;
        }

        if (Seeds is < 0 or > 100)
        {
            return ValidationResult.Error("The seed count must be 0 to 100.");
        }

        return Scale is > 0 and <= 1 ? ValidationResult.Success() : ValidationResult.Error("The scale must be above 0 and at most 1.");
    }
}

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class AnalyzeSettings : GameSettings
{
    [CommandArgument(0, "[MAPS]")]
    [Description("The maps to analyze, relative to the Maps folder, such as Level_03.map or ADDON/Level_02.map. Defaults to campaign levels 2 to 10 and the levels 2 to 5 of both add-ons; with --levels, to the campaign levels found there.")]
    public string[] Maps { get; init; } = [];

    [CommandOption("-o|--out <FOLDER>", true)]
    [Description("The folder for routes.json and the level images. It is created when missing.")]
    public string Out { get; init; } = "";

    [CommandOption("-l|--levels <FOLDER>")]
    [Description("A folder of generated maps and their .lgc scripts to analyze instead of the originals, such as the game's Maps folder after generating. Without it, the original levels are analyzed.")]
    public string? Levels { get; init; }

    [CommandOption("-s|--scale <SCALE>")]
    [Description("Image pixels per map pixel, above 0 and at most 1. Markers and lines grow with it.")]
    [DefaultValue(1.0)]
    public double Scale { get; init; } = 1;

    [CommandOption("--game-lighting")]
    [Description("Lights Alien Shooter 2 levels as the game does, with the ambient light of each map and the omni-lights of its objects.")]
    public bool GameLighting { get; init; }

    [CommandOption("--no-ambient-tint")]
    [Description("With --game-lighting, keeps the brightness of the ambient light but drops its hue.")]
    public bool NoAmbientTint { get; init; }

    [CommandOption("--no-images")]
    [Description("Writes routes.json only.")]
    public bool NoImages { get; init; }

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

using System.ComponentModel;
using ASToolkit.Core.Generation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class PreviewSettings : GameSettings
{
    [CommandOption("-s|--seed <SEED>", true)]
    [Description("The seed.")]
    public int Seed { get; init; }

    [CommandOption("-l|--level <LEVEL>", true)]
    [Description("The campaign level, 2 to 10, or the add-on level, 1 to 5.")]
    public int Level { get; init; }

    [CommandOption("--addon-style")]
    [Description("Generate an add-on level, 1 to 5, instead of a campaign level.")]
    public bool AddOnStyle { get; init; }

    [CommandOption("-o|--out <MAP>", true)]
    [Description("The map file to write; the script is written next to it.")]
    public string Out { get; init; } = "";

    public override ValidationResult Validate()
    {
        var game = base.Validate();
        if (!game.Successful)
        {
            return game;
        }

        return AddOnStyle
            ? Level is >= 1 and <= LevelGenerator.AddOnLevelCount ? ValidationResult.Success() : ValidationResult.Error("The add-on level must be 1 to 5.")
            : Level is >= 2 and <= 10 ? ValidationResult.Success() : ValidationResult.Error("The level must be 2 to 10.");
    }
}

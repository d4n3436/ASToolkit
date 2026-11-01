using System.ComponentModel;
using ASToolkit.Core.Reference;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class PreviewSettings : GameSettings
{
    [CommandOption("-s|--seed <SEED>", true)]
    [Description("The seed.")]
    public int Seed { get; init; }

    [CommandOption("-l|--level <LEVEL>", true)]
    [Description("The campaign level, 1 to 10, or the add-on level, 1 to 5.")]
    public int Level { get; init; }

    [CommandOption("--addon-style")]
    [Description("Generate an add-on level, 1 to 5, instead of a campaign level.")]
    public bool AddOnStyle { get; init; }

    public GenerationStyle Style => AddOnStyle ? GenerationStyle.AddOn : GenerationStyle.Campaign;

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
            ? Level >= 1 && Level <= GenerationStyle.AddOn.LevelCount ? ValidationResult.Success() : ValidationResult.Error("The add-on level must be 1 to 5.")
            : Level >= 1 && Level <= GenerationStyle.Campaign.LevelCount ? ValidationResult.Success() : ValidationResult.Error("The level must be 1 to 10.");
    }
}

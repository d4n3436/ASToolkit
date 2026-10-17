using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal class GameSettings : CommandSettings
{
    [CommandOption("-g|--game <FOLDER>", true)]
    [Description("The game folder, the one that holds objects.res and Maps.")]
    public string Game { get; init; } = "";

    public override ValidationResult Validate() =>
        File.Exists(Path.Combine(Game, "objects.res"))
            ? ValidationResult.Success()
            : ValidationResult.Error($"No objects.res in {Game}.");
}

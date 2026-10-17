using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class CheckSettings : GameSettings
{
    [CommandOption("-l|--levels <FOLDER>")]
    [Description("The folder with the Level_NN.map and .lgc files to check. Defaults to the game's Maps folder.")]
    public string? Levels { get; init; }

    [CommandOption("-v|--verbose")]
    [Description("Lists every rule, not only the broken ones.")]
    public bool Verbose { get; init; }

    public override ValidationResult Validate()
    {
        var game = base.Validate();
        if (!game.Successful || Levels is null || Directory.Exists(Levels))
        {
            return game;
        }

        return ValidationResult.Error($"No folder {Levels}.");
    }
}

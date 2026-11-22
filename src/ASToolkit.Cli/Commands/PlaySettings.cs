using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class PlaySettings : GameSettings
{
    [CommandOption("--campaign <NAME>")]
    [Description("The campaign to play, one of original, addon, addon2 or all.")]
    [DefaultValue("all")]
    public string Campaign { get; init; } = "all";

    [CommandOption("--start <LEVEL>")]
    [Description("The first level to play. Equipment and earnings carry between subsequent levels.")]
    [DefaultValue(1)]
    public int Start { get; init; } = 1;

    [CommandOption("--seed <SEED>")]
    [DefaultValue(1)]
    public int Seed { get; init; } = 1;

    [CommandOption("--money <AMOUNT>")]
    [Description("The cash before the first shop. Defaults to the campaign's starting cash and the preceding level's reward.")]
    public int? Money { get; init; }

    [CommandOption("--out <FILE>")]
    [Description("An optional JSON report of the results, loadouts and timings.")]
    public string? Out { get; init; }

    public override ValidationResult Validate()
    {
        var game = base.Validate();

        if (!game.Successful)
        {
            return game;
        }

        if (Campaign is not ("original" or "addon" or "addon2" or "all"))
        {
            return ValidationResult.Error("Choose original, addon, addon2, or all.");
        }

        int last = Campaign == "original" ? 10 : 5;

        return Start >= 1 && Start <= last && Money is not < 0 ? ValidationResult.Success()
            : ValidationResult.Error($"Choose a starting level from 1 through {last} and a nonnegative cash amount.");
    }
}

using System.ComponentModel;
using ASToolkit.Core.Generation.Layout;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class GenerateSettings : GameSettings
{
    [CommandOption("-s|--seed <SEED>", true)]
    [Description("The seed.")]
    public int Seed { get; init; }

    [CommandOption("--first <LEVEL>")]
    [Description("The first campaign level to generate; levels outside --first to --last are the campaign's own. Defaults to 2.")]
    public int First { get; init; } = 2;

    [CommandOption("--last <LEVEL>")]
    [Description("The last campaign level to generate. Defaults to 10.")]
    public int Last { get; init; } = 10;

    [CommandOption("--addon-style")]
    [Description("Generate the five levels of an add-on instead of the ten of the campaign; --first and --last are ignored. Either way the levels become the RANDOM add-on on the play menu.")]
    public bool AddOnStyle { get; init; }

    [CommandOption("--layout <STYLE>")]
    [Description("The layout style, room for a complex of rooms of varied sizes or open for bigger rooms joined by wide connections. Defaults to room.")]
    public LayoutStyle Layout { get; init; } = LayoutStyle.Room;

    public override ValidationResult Validate()
    {
        var game = base.Validate();
        if (!game.Successful)
        {
            return game;
        }

        return AddOnStyle || First is >= 2 and <= 10 && Last is >= 2 and <= 10 && First <= Last
            ? ValidationResult.Success()
            : ValidationResult.Error("The levels must lie between 2 and 10.");
    }
}

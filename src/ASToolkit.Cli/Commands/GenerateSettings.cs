using System.CommandLine;
using ASToolkit.Analysis.Reference;

namespace ASToolkit.Cli.Commands;

internal sealed class GenerateSettings : GameSettings, ICommandSettings<GenerateSettings>
{
    private static readonly Option<int> SeedOption = new("--seed", "-s") { Description = "The seed.", HelpName = "SEED", Required = true };

    private static readonly Option<int> FirstOption = new("--first")
    {
        Description = "The first campaign level to generate; levels outside --first to --last are the campaign's own. Defaults to 1.",
        HelpName = "LEVEL",
        DefaultValueFactory = _ => 1
    };

    private static readonly Option<int> LastOption = new("--last") { Description = "The last campaign level to generate. Defaults to 10.", HelpName = "LEVEL", DefaultValueFactory = _ => 10 };

    private static readonly Option<bool> AddOnStyleOption = new("--addon-style")
    {
        Description = "Generate the five levels of an add-on instead of the ten of the campaign; --first and --last are ignored. Either way the levels become the RANDOM add-on on the play menu."
    };

    public GenerateSettings(ParseResult result) : base(result)
    {
        Seed = result.GetRequiredValue(SeedOption);
        First = result.GetValue(FirstOption);
        Last = result.GetValue(LastOption);
        AddOnStyle = result.GetValue(AddOnStyleOption);
    }

    public int Seed { get; }

    public int First { get; }

    public int Last { get; }

    public bool AddOnStyle { get; }

    public GenerationStyle Style => AddOnStyle ? GenerationStyle.AddOn : GenerationStyle.Campaign;

    static IReadOnlyList<Symbol> ICommandSettings<GenerateSettings>.Symbols => [GameOption, SeedOption, FirstOption, LastOption, AddOnStyleOption];

    static GenerateSettings ICommandSettings<GenerateSettings>.Read(ParseResult result) => new(result);

    public override string? GetValidationError() => base.GetValidationError()
        ?? (AddOnStyle || First >= 1 && Last <= GenerationStyle.Campaign.LevelCount && First <= Last ? null : "The levels must lie between 1 and 10.");
}

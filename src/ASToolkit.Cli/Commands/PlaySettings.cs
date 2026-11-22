using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class PlaySettings : GameSettings, ICommandSettings<PlaySettings>
{
    private static readonly Option<string> CampaignOption = new("--campaign")
    {
        Description = "The campaign to play, one of original, addon, addon2 or all.",
        HelpName = "NAME",
        DefaultValueFactory = _ => "all"
    };

    private static readonly Option<int> StartOption = new("--start")
    {
        Description = "The first level to play. Equipment and earnings carry between subsequent levels.",
        HelpName = "LEVEL",
        DefaultValueFactory = _ => 1
    };

    private static readonly Option<int> SeedOption = new("--seed") { HelpName = "SEED", DefaultValueFactory = _ => 1 };

    private static readonly Option<int?> MoneyOption = new("--money")
    {
        Description = "The cash before the first shop. Defaults to the campaign's starting cash and the preceding level's reward.",
        HelpName = "AMOUNT"
    };

    private static readonly Option<string?> OutOption = new("--out") { Description = "An optional JSON report of the results, loadouts and timings.", HelpName = "FILE" };

    public PlaySettings(ParseResult result) : base(result)
    {
        Campaign = result.GetRequiredValue(CampaignOption);
        Start = result.GetValue(StartOption);
        Seed = result.GetValue(SeedOption);
        Money = result.GetValue(MoneyOption);
        Out = result.GetValue(OutOption);
    }

    public string Campaign { get; }

    public int Start { get; }

    public int Seed { get; }

    public int? Money { get; }

    public string? Out { get; }

    static IReadOnlyList<Symbol> ICommandSettings<PlaySettings>.Symbols => [GameOption, CampaignOption, StartOption, SeedOption, MoneyOption, OutOption];

    static PlaySettings ICommandSettings<PlaySettings>.Read(ParseResult result) => new(result);

    public override string? GetValidationError()
    {
        string? error = base.GetValidationError();
        if (error is not null)
        {
            return error;
        }

        if (Campaign is not ("original" or "addon" or "addon2" or "all"))
        {
            return "Choose original, addon, addon2, or all.";
        }

        int last = Campaign == "original" ? 10 : 5;
        return Start >= 1 && Start <= last && Money is not < 0 ? null : $"Choose a starting level from 1 through {last} and a nonnegative cash amount.";
    }
}

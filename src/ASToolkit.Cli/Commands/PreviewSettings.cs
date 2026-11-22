using System.CommandLine;
using ASToolkit.Analysis.Reference;

namespace ASToolkit.Cli.Commands;

internal sealed class PreviewSettings : GameSettings, ICommandSettings<PreviewSettings>
{
    private static readonly Option<int> SeedOption = new("--seed", "-s") { Description = "The seed.", HelpName = "SEED", Required = true };

    private static readonly Option<int> LevelOption = new("--level", "-l") { Description = "The campaign level, 1 to 10, or the add-on level, 1 to 5.", HelpName = "LEVEL", Required = true };

    private static readonly Option<bool> AddOnStyleOption = new("--addon-style") { Description = "Generate an add-on level, 1 to 5, instead of a campaign level." };

    private static readonly Option<string> OutOption = new("--out", "-o") { Description = "The map file to write; the script is written next to it.", HelpName = "MAP", Required = true };

    public PreviewSettings(ParseResult result) : base(result)
    {
        Seed = result.GetRequiredValue(SeedOption);
        Level = result.GetRequiredValue(LevelOption);
        AddOnStyle = result.GetValue(AddOnStyleOption);
        Out = result.GetRequiredValue(OutOption);
    }

    public int Seed { get; }

    public int Level { get; }

    public bool AddOnStyle { get; }

    public GenerationStyle Style => AddOnStyle ? GenerationStyle.AddOn : GenerationStyle.Campaign;

    public string Out { get; }

    static IReadOnlyList<Symbol> ICommandSettings<PreviewSettings>.Symbols => [GameOption, SeedOption, LevelOption, AddOnStyleOption, OutOption];

    static PreviewSettings ICommandSettings<PreviewSettings>.Read(ParseResult result) => new(result);

    public override string? GetValidationError() => base.GetValidationError() ?? (AddOnStyle
        ? Level >= 1 && Level <= GenerationStyle.AddOn.LevelCount ? null : "The add-on level must be between 1 and 5."
        : Level >= 1 && Level <= GenerationStyle.Campaign.LevelCount ? null : "The level must be between 1 and 10.");
}

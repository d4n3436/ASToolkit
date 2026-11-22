using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectLevelSettings : GameSettings, ICommandSettings<InspectLevelSettings>
{
    private static readonly Argument<string[]> MapsArgument = new("MAPS")
    {
        Description = "The maps to survey, relative to the Maps folder, such as Level_03.map or ADDON/Level_02.map. Defaults to campaign levels 2 to 10 and the levels 2 to 5 of both add-ons; with --levels, to the campaign levels found there.",
        Arity = ArgumentArity.ZeroOrMore
    };

    private static readonly Option<string> OutOption = new("--out", "-o") { Description = "The folder for the place images. It is created if it doesn't exist.", HelpName = "FOLDER", Required = true };

    private static readonly Option<string?> LevelsOption = new("--levels", "-l") { Description = "A folder of generated maps and their .lgc scripts to read instead of the originals.", HelpName = "FOLDER" };

    private static readonly Option<float> ScaleOption = new("--scale", "-s")
    {
        Description = "Image pixels per map pixel of the place images, above 0 and at most 1.",
        HelpName = "SCALE",
        DefaultValueFactory = _ => 0.5f
    };

    private static readonly Option<bool> NoImagesOption = new("--no-images") { Description = "Prints the table only, without the place images." };

    public InspectLevelSettings(ParseResult result) : base(result)
    {
        Maps = result.GetValue(MapsArgument) ?? [];
        Out = result.GetRequiredValue(OutOption);
        Levels = result.GetValue(LevelsOption);
        Scale = result.GetValue(ScaleOption);
        NoImages = result.GetValue(NoImagesOption);
    }

    public string[] Maps { get; }

    public string Out { get; }

    public string? Levels { get; }

    public float Scale { get; }

    public bool NoImages { get; }

    static IReadOnlyList<Symbol> ICommandSettings<InspectLevelSettings>.Symbols => [GameOption, MapsArgument, OutOption, LevelsOption, ScaleOption, NoImagesOption];

    static InspectLevelSettings ICommandSettings<InspectLevelSettings>.Read(ParseResult result) => new(result);

    public override string? GetValidationError() => base.GetValidationError()
        ?? (Levels is not null && !Directory.Exists(Levels) ? $"No folder {Levels}." : Scale is > 0 and <= 1 ? null : "The scale must be above 0 and at most 1.");
}

using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class AnalyzeSettings : GameSettings, ICommandSettings<AnalyzeSettings>
{
    private static readonly Argument<string[]> MapsArgument = new("MAPS")
    {
        Description = "The maps to analyze, relative to the Maps folder, such as Level_03.map or ADDON/Level_02.map. Defaults to campaign levels 2 to 10 and the levels 2 to 5 of both add-ons, or to every Level_*.map of Alien Shooter 2; with --levels, to the campaign levels found there.",
        Arity = ArgumentArity.ZeroOrMore
    };

    private static readonly Option<string> OutOption = new("--out", "-o") { Description = "The folder for routes.json and the level images. It is created if it doesn't exist.", HelpName = "FOLDER", Required = true };

    private static readonly Option<string?> LevelsOption = new("--levels", "-l")
    {
        Description = "A folder of generated maps and their .lgc scripts to analyze instead of the originals, such as the game's Maps folder after generating. Without it, the original levels are analyzed.",
        HelpName = "FOLDER"
    };

    private static readonly Option<float> ScaleOption = new("--scale", "-s") { Description = "Image pixels per map pixel, above 0 and at most 1. Markers and lines grow with it.", HelpName = "SCALE", DefaultValueFactory = _ => 1 };

    private static readonly Option<bool> NoImagesOption = new("--no-images") { Description = "Writes routes.json only." };

    public AnalyzeSettings(ParseResult result) : base(result)
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

    static IReadOnlyList<Symbol> ICommandSettings<AnalyzeSettings>.Symbols => [GameOption, MapsArgument, OutOption, LevelsOption, ScaleOption, NoImagesOption];

    static AnalyzeSettings ICommandSettings<AnalyzeSettings>.Read(ParseResult result) => new(result);

    public override string? GetValidationError() => base.GetValidationError()
        ?? (Levels is not null && !Directory.Exists(Levels) ? $"No folder {Levels}." : Scale is > 0 and <= 1 ? null : "The scale must be above 0 and at most 1.");
}

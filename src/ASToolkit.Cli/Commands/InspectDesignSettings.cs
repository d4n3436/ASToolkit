using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectDesignSettings : GameSettings, ICommandSettings<InspectDesignSettings>
{
    private static readonly Option<string> OutOption = new("--out", "-o")
    {
        Description = "The folder for design.tsv, design.md and the generated levels. It is created if it doesn't exist.",
        HelpName = "FOLDER",
        Required = true
    };

    private static readonly Option<int> SeedsOption = new("--seeds")
    {
        Description = "How many seeds of campaign levels 2 to 10 to generate and measure against the originals. 0 measures the originals only.",
        HelpName = "COUNT",
        DefaultValueFactory = _ => 10
    };

    private static readonly Option<bool> LeaveOneOutOption = new("--leave-one-out")
    {
        Description = "Also refits the generator without each campaign original in turn, generates the seeds again and writes leave-one-out.md with the rows that left the originals' range."
    };

    private static readonly Option<bool> ImagesOption = new("--images") { Description = "Writes an image of the places of every measured level." };

    private static readonly Option<float> ScaleOption = new("--scale", "-s")
    {
        Description = "Image pixels per map pixel of the place images, above 0 and at most 1.",
        HelpName = "SCALE",
        DefaultValueFactory = _ => 0.15f
    };

    public InspectDesignSettings(ParseResult result) : base(result)
    {
        Out = result.GetRequiredValue(OutOption);
        Seeds = result.GetValue(SeedsOption);
        LeaveOneOut = result.GetValue(LeaveOneOutOption);
        Images = result.GetValue(ImagesOption);
        Scale = result.GetValue(ScaleOption);
    }

    public string Out { get; }

    public int Seeds { get; }

    public bool LeaveOneOut { get; }

    public bool Images { get; }

    public float Scale { get; }

    static IReadOnlyList<Symbol> ICommandSettings<InspectDesignSettings>.Symbols => [GameOption, OutOption, SeedsOption, LeaveOneOutOption, ImagesOption, ScaleOption];

    static InspectDesignSettings ICommandSettings<InspectDesignSettings>.Read(ParseResult result) => new(result);

    public override string? GetValidationError() => base.GetValidationError()
        ?? (Seeds is < 0 or > 100 ? "The seed count must be between 0 and 100." : Scale is > 0 and <= 1 ? null : "The scale must be above 0 and at most 1.");
}

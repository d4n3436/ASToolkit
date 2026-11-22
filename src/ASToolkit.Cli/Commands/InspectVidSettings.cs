using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectVidSettings : GameSettings, ICommandSettings<InspectVidSettings>
{
    private static readonly Argument<string> FileArgument = new("FILE") { Description = "The sprite file, relative to the game folder or a path." };

    private static readonly Option<int> FrameOption = new("--frame") { Description = "The frame to read.", HelpName = "INDEX" };

    private static readonly Option<bool> PointsOption = new("--points") { Description = "Lists every height point of the frame." };

    public InspectVidSettings(ParseResult result) : base(result)
    {
        File = result.GetRequiredValue(FileArgument);
        Frame = result.GetValue(FrameOption);
        Points = result.GetValue(PointsOption);
    }

    public string File { get; }

    public int Frame { get; }

    public bool Points { get; }

    static IReadOnlyList<Symbol> ICommandSettings<InspectVidSettings>.Symbols => [GameOption, FileArgument, FrameOption, PointsOption];

    static InspectVidSettings ICommandSettings<InspectVidSettings>.Read(ParseResult result) => new(result);
}

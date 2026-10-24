using System.ComponentModel;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectVidSettings : GameSettings
{
    [CommandArgument(0, "<FILE>")]
    [Description("The sprite file, relative to the game folder or a path.")]
    public string File { get; init; } = "";

    [CommandOption("--frame <INDEX>")]
    [Description("The frame to read.")]
    [DefaultValue(0)]
    public int Frame { get; init; }

    [CommandOption("--points")]
    [Description("Lists every height point of the frame.")]
    public bool Points { get; init; }
}

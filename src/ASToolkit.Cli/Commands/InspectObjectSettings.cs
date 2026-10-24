using System.ComponentModel;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectObjectSettings : GameSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("The object ID.")]
    public int Id { get; init; }
}

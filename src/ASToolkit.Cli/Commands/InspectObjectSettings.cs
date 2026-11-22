using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectObjectSettings : GameSettings, ICommandSettings<InspectObjectSettings>
{
    private static readonly Argument<int> IdArgument = new("ID") { Description = "The object ID." };

    public InspectObjectSettings(ParseResult result) : base(result)
    {
        Id = result.GetValue(IdArgument);
    }

    public int Id { get; }

    static IReadOnlyList<Symbol> ICommandSettings<InspectObjectSettings>.Symbols => [GameOption, IdArgument];

    static InspectObjectSettings ICommandSettings<InspectObjectSettings>.Read(ParseResult result) => new(result);
}

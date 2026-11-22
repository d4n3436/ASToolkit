using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal interface ICommandSettings<out TSelf> where TSelf : ICommandSettings<TSelf>
{
    static abstract IReadOnlyList<Symbol> Symbols { get; }

    static abstract TSelf Read(ParseResult result);
}

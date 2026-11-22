using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal class GameSettings : ICommandSettings<GameSettings>
{
    protected static readonly Option<string> GameOption = new("--game", "-g") { Description = "The game folder, which holds objects.res and Maps.", HelpName = "FOLDER", Required = true };

    public GameSettings(ParseResult result)
    {
        Game = result.GetRequiredValue(GameOption);
    }

    public string Game { get; }

    static IReadOnlyList<Symbol> ICommandSettings<GameSettings>.Symbols => [GameOption];

    static GameSettings ICommandSettings<GameSettings>.Read(ParseResult result) => new(result);

    public virtual string? GetValidationError() => File.Exists(Path.Combine(Game, "objects.res")) ? null : $"Could not find objects.res in '{Game}'.";
}

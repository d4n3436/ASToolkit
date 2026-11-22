using System.CommandLine;

namespace ASToolkit.Cli.Commands;

internal sealed class SpriteSelectionSettings : GameSettings, ICommandSettings<SpriteSelectionSettings>
{
    private static readonly Argument<string> MapArgument = new("MAP") { Description = "The map to read, relative to the Maps folder or a path to a map file." };

    private static readonly Option<int?> IdOption = new("--id") { Description = "Keeps the sprites of one object ID.", HelpName = "ID" };

    private static readonly Option<string?> NameOption = new("--name") { Description = "Keeps the sprites whose object name or sprite name contains the text.", HelpName = "TEXT" };

    private static readonly Option<MapArea?> NearOption = new("--near")
    {
        Description = "Keeps the sprites within a radius of a point, in map pixels.",
        HelpName = "X,Y,R",
        CustomParser = result =>
        {
            if (MapArea.TryParse(result.Tokens[0].Value, out var area))
            {
                return area;
            }

            result.AddError("The area must be in the format x,y,r, such as 3200,4100,300.");
            return null;
        }
    };

    public SpriteSelectionSettings(ParseResult result) : base(result)
    {
        Map = result.GetRequiredValue(MapArgument);
        Id = result.GetValue(IdOption);
        Name = result.GetValue(NameOption);
        Near = result.GetValue(NearOption);
    }

    public string Map { get; }

    public int? Id { get; }

    public string? Name { get; }

    public MapArea? Near { get; }

    static IReadOnlyList<Symbol> ICommandSettings<SpriteSelectionSettings>.Symbols => [GameOption, MapArgument, IdOption, NameOption, NearOption];

    static SpriteSelectionSettings ICommandSettings<SpriteSelectionSettings>.Read(ParseResult result) => new(result);
}

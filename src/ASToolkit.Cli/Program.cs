using System.CommandLine;
using ASToolkit.Cli.Commands;

var root = new RootCommand("Generates, inspects, analyzes and plays Alien Shooter levels.")
{
    CreateCommand("generate", "Generates levels into the game as the RANDOM add-on and adds it to the play menu.", new GenerateCommand()),
    CreateCommand("restore", "Removes the RANDOM add-on and the script and menu changes it made.", new RestoreCommand()),
    CreateCommand("preview", "Generates one level into a map file.", new PreviewCommand()),
    new Command("widescreen", "Adds or removes the widescreen mod.")
    {
        CreateCommand("apply", "Adds widescreen resolutions, a borderless window and a HUD that fits them.", new WidescreenApplyCommand()),
        CreateCommand("restore", "Puts the executable, HUD and camera scripts back.", new WidescreenRestoreCommand())
    },
    new Command("inspect", "Prints the objects, sprites, action stacks, sprite files and level places of a game.")
    {
        CreateCommand("object", "Prints every field of an object, its link chain and whether it blocks the player.", new InspectObjectCommand()),
        CreateCommand("sprites", "Lists the sprites of a map with their army, facing, sprite name and the openings the scripts give them.", new InspectSpritesCommand()),
        CreateCommand("stacks", "Lists the action stacks of the sprites of a map in run order.", new InspectStacksCommand()),
        CreateCommand("level", "Surveys levels into their places, connections and gates, with an image of the places.", new InspectLevelCommand()),
        CreateCommand("rooms", "Writes a cropped image of every room of the original levels with its role and type, and the tables of types by role and level.", new InspectRoomsCommand()),
        CreateCommand("design", "Measures the design of the original levels and of generated ones, such as furniture, pickups, walls and pacing, into design.tsv and design.md.", new InspectDesignCommand()),
        CreateCommand("vid", "Prints the flags, format, frames, depth histogram and height points of a sprite file.", new InspectVidCommand())
    },
    CreateCommand("analyze", "Analyzes the routes of original or generated levels into routes.json and map images with the tour drawn on them.", new AnalyzeCommand()),
    CreateCommand("play", "Plays the original campaigns in order with retained equipment and shopping.", new PlayCommand())
};

return await root.Parse(args).InvokeAsync();

static Command CreateCommand<TSettings>(string name, string description, Command<TSettings> command) where TSettings : GameSettings, ICommandSettings<TSettings>
{
    var created = new Command(name, description);
    foreach (var symbol in TSettings.Symbols)
    {
        if (symbol is Option option)
        {
            created.Options.Add(option);
        }
        else
        {
            created.Arguments.Add((Argument)symbol);
        }
    }

    created.SetAction((result, cancellationToken) =>
    {
        var settings = TSettings.Read(result);
        string? error = settings.GetValidationError();
        if (error is null)
        {
            return Task.FromResult(command.Execute(settings, cancellationToken));
        }

        Console.Error.WriteLine(error);
        return Task.FromResult(1);
    });

    return created;
}

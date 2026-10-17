using ASToolkit.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("ascli");
    config.SetExceptionHandler((exception, _) =>
    {
        AnsiConsole.WriteException(exception, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        return 1;
    });
    config.AddCommand<GenerateCommand>("generate").WithDescription("Generates levels into the game as the RANDOM add-on and adds it to the play menu.");
    config.AddCommand<RestoreCommand>("restore").WithDescription("Removes the RANDOM add-on and the script and menu changes it made.");
    config.AddCommand<PreviewCommand>("preview").WithDescription("Generates one level into a map file.");
    config.AddCommand<CheckCommand>("check").WithDescription("Checks campaign levels 2 to 10 against the level rules.");
    config.AddBranch("widescreen", widescreen =>
    {
        widescreen.SetDescription("Adds or removes the widescreen mod.");
        widescreen.AddCommand<WidescreenApplyCommand>("apply").WithDescription("Adds widescreen resolutions, a borderless window and a HUD that fits them.");
        widescreen.AddCommand<WidescreenRestoreCommand>("restore").WithDescription("Puts the executable, HUD and camera scripts back.");
    });
    config.AddCommand<AnalyzeCommand>("analyze").WithDescription("Analyzes the routes of original or generated levels into routes.json and map images with the tour drawn on them.");
});

return app.Run(args);

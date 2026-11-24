using ASToolkit.Cli.Analysis;
using ASToolkit.Analysis.Survey;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Bot;
using ASToolkit.Analysis.Reference;
using ASToolkit.Bot.Rendering;
using SixLabors.ImageSharp;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectLevelCommand : LevelCommand<InspectLevelSettings>
{
    public override int Execute(InspectLevelSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        string[] maps = GetMaps(settings.Maps, settings.Levels);
        Directory.CreateDirectory(settings.Out);
        string?[] problems = new string?[maps.Length];
        var surveys = new LevelSurvey?[maps.Length];
        var loader = new LevelLoader(objects);
        AnsiConsole.Status().Start("Surveying the levels", _ => Parallel.For(0, maps.Length, CreateParallelOptions(cancellationToken), mapIndex =>
        {
            string folder = settings.Levels ?? Path.Combine(settings.Game, "Maps");
            string mapPath = Path.Combine(folder, maps[mapIndex]);
            if (!File.Exists(mapPath))
            {
                problems[mapIndex] = "missing";
                return;
            }

            var level = loader.Load(maps[mapIndex], mapPath, settings.Levels is null);
            if (level is null)
            {
                problems[mapIndex] = "no route";
                return;
            }

            var survey = level.Survey;
            surveys[mapIndex] = survey;
            if (!settings.NoImages)
            {
                string name = (settings.Levels is null ? "" : "gen_") + Path.ChangeExtension(maps[mapIndex], null).Replace('/', '_').Replace('\\', '_');
                using var rendered = MapImage.Render(Playthrough.LoadMap(settings.Game, objects, mapPath), settings.Scale);
                PlaceOverlay.Draw(rendered, survey, level.Analysis);
                rendered.Image.SaveAsPng(Path.Combine(settings.Out, name + "_places.png"), PngOptions.Fast);
            }
        }));

        var table = new Table().AddColumns("Map", "Places", "Route places", "Loops", "Gates", "Off-route floor");
        for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
        {
            var survey = surveys[mapIndex];
            if (survey is null)
            {
                table.AddRow(Markup.Escape(maps[mapIndex]), $"[red]{problems[mapIndex]}[/]", "", "", "", "");
                continue;
            }

            var signature = survey.Signature;
            table.AddRow(Markup.Escape(maps[mapIndex]), signature.Places.ToString(), signature.RoutePlaces.ToString(), signature.Loops.ToString(),
                survey.Gates.Count.ToString(), $"{signature.OffRouteShare:P0}");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Wrote the place maps to [green]{Markup.Escape(settings.Out)}[/].");
        return problems.All(problem => problem is null) ? 0 : 1;
    }
}

using ASToolkit.Cli.Analysis;
using ASToolkit.Core.Survey;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Reference;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectLevelCommand : Command<InspectLevelSettings>
{
    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private const int MaxLevelsAtOnce = 3;

    private static readonly PngEncoder PngOptions = new() { CompressionLevel = PngCompressionLevel.BestSpeed };

    public override int Execute(CommandContext context, InspectLevelSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        string[] maps = GetMaps(settings);
        Directory.CreateDirectory(settings.Out);
        var renderer = settings.NoImages ? null : new MapRenderer(settings.Game, objects);
        string?[] problems = new string?[maps.Length];
        var surveys = new LevelSurvey?[maps.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        var loader = new LevelLoader(objects);
        AnsiConsole.Status().Start("Surveying the levels", _ => Parallel.For(0, maps.Length, options, mapIndex =>
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
            if (renderer is not null)
            {
                string name = (settings.Levels is null ? "" : "gen_") + Path.ChangeExtension(maps[mapIndex], null).Replace('/', '_').Replace('\\', '_');
                using var rendered = renderer.Render(level.Map.Sprites, settings.Scale, level.Map.Graphics);
                PlaceOverlay.Draw(rendered, survey, level.Analysis);
                rendered.Image.SaveAsPng(Path.Combine(settings.Out, name + "_places.png"), PngOptions);
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

    private static string[] GetMaps(InspectLevelSettings settings)
    {
        if (settings.Maps.Length > 0)
        {
            return settings.Maps;
        }

        var campaign = ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName);
        return settings.Levels is null
            ? campaign.Concat(AddOnLevels).ToArray()
            : campaign.Where(map => File.Exists(Path.Combine(settings.Levels, map))).ToArray();
    }
}

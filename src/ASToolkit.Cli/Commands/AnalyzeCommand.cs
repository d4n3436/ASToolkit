using System.Text.Json;
using System.Text.Json.Serialization;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules;
using ASToolkit.Cli.Analysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class AnalyzeCommand : Command<AnalyzeSettings>
{

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private const int MaxLevelsAtOnce = 3;

    private static readonly PngEncoder PngOptions = new() { CompressionLevel = PngCompressionLevel.BestSpeed };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public override int Execute(CommandContext context, AnalyzeSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        string[] maps = GetMaps(settings);
        Directory.CreateDirectory(settings.Out);
        var renderer = settings.NoImages ? null : new MapRenderer(settings.Game, objects) { MatchGameLighting = settings.GameLighting, OmitAmbientTint = settings.NoAmbientTint };
        var reports = new RouteReport?[maps.Length];
        string?[] problems = new string?[maps.Length];
        string?[] images = new string?[maps.Length];
        int[] undrawn = new int[maps.Length];
        bool[] noScript = new bool[maps.Length];
        
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        AnsiConsole.Status().Start("Analyzing the levels", _ => Parallel.For(0, maps.Length, options, i =>
        {
            (string mapPath, string scriptPath) = Locate(settings, maps[i]);
            if (!File.Exists(mapPath))
            {
                problems[i] = "missing";
                return;
            }

            var map = MapResourceBuilder.Load(mapPath);
            noScript[i] = !File.Exists(scriptPath);
            var script = LevelScriptReader.Read(scriptPath);
            var analysis = RouteAnalyzer.Analyze(maps[i], map, script, objects);

            string? image = null;
            if (renderer is not null)
            {
                image = (settings.Levels is null ? "" : "gen_") + Path.ChangeExtension(maps[i], null).Replace('/', '_').Replace('\\', '_') + ".png";
                images[i] = image;
                using var rendered = renderer.Render(map.Sprites, settings.Scale, map.Graphics);
                if (analysis is not null)
                {
                    RouteOverlay.Draw(rendered, analysis, script);
                }

                rendered.Image.SaveAsPng(Path.Combine(settings.Out, image), PngOptions);
                undrawn[i] = rendered.Undrawn;
            }

            if (analysis is null)
            {
                problems[i] = "no route";
                return;
            }

            reports[i] = ToReport(maps[i], settings.Levels is not null, image, analysis);
        }));

        var table = new Table().AddColumns("Map", "Theme", "Tour", "Spine share", "Elongation", "Image");
        for (int mapIndex = 0; mapIndex < maps.Length; mapIndex++)
        {
            var r = reports[mapIndex];
            string? image = images[mapIndex];
            table.AddRow(Markup.Escape(maps[mapIndex]), r?.Theme.ToString() ?? "", r is null ? $"[red]{problems[mapIndex]}[/]" : $"{r.TourPixels} px",
                r?.SpineShare.ToString("0.00") ?? "", r?.Elongation.ToString("0.00") ?? "",
                image is null ? "" : Markup.Escape(image) + (undrawn[mapIndex] > 0 ? $" [grey]({undrawn[mapIndex]} sprites undrawn)[/]" : ""));
        }

        AnsiConsole.Write(table);
        foreach (int mapIndex in Enumerable.Range(0, maps.Length).Where(i => noScript[i]))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(maps[mapIndex])} has no .lgc script next to it, so its gates, blackout and checkpoints are left out.[/]");
        }

        string json = Path.Combine(settings.Out, settings.Levels is null ? "routes.json" : "routes_generated.json");
        File.WriteAllText(json, JsonSerializer.Serialize(reports.OfType<RouteReport>(), JsonOptions));
        AnsiConsole.MarkupLine($"Wrote [green]{Markup.Escape(json)}[/].");
        return problems.All(p => p is null) ? 0 : 1;
    }

    private static string[] GetMaps(AnalyzeSettings settings)
    {
        if (settings.Maps.Length > 0)
        {
            return settings.Maps;
        }

        var campaign = ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName);
        return settings.Levels is null
            ? campaign.Concat(AddOnLevels).ToArray()
            : campaign.Where(m => File.Exists(Path.Combine(settings.Levels, m))).ToArray();
    }

    private static (string Map, string Script) Locate(AnalyzeSettings settings, string map)
    {
        string script = Path.ChangeExtension(map, ".lgc");
        return settings.Levels is null
            ? (Path.Combine(settings.Game, "Maps", map), Path.Combine(settings.Game, "Maps", script))
            : (Path.Combine(settings.Levels, map), Path.Combine(settings.Levels, script));
    }

    private static RouteReport ToReport(string map, bool generated, string? image, RouteAnalysis a)
    {
        var lattice = a.Grid.Lattice;
        var elements = a.Elements.Select(e => new ElementReport(e.Kind, (int)e.Id, e.X, e.Y, e.Hidden, (int)e.CrateItem, Math.Round(e.Progress, 4), e.Distance, e.BranchDepth,
            e.Program?.Trigger is { } trigger ? [trigger.X, trigger.Y, trigger.Radius] : [])).ToArray();
        
        int[][] tour = a.Tour.Select(lattice.GetCenter).Select(p => new[] { (int)Math.Round(p.X), (int)Math.Round(p.Y) }).ToArray();
        return new RouteReport(map, generated, image, a.Theme, a.RoutePixels, a.SpinePixels, Math.Round(a.SpinePixels / (double)Math.Max(1, a.RoutePixels), 4),
            Math.Round(ReferenceLevelSet.GetElongation(a), 4), Math.Round(a.RevisitShare, 4), a.ReachableCells, a.Legs, a.Objectives, a.Checkpoints, elements,
            a.Gating, a.FallbackUsage, tour);
    }
}

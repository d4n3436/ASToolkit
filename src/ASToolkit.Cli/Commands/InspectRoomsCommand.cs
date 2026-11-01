using System.Text;
using ASToolkit.Cli.Analysis;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Reference;
using SixLabors.ImageSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectRoomsCommand : Command<InspectRoomsSettings>
{
    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private const int MaxLevelsAtOnce = 3;
    private const float MinListedShare = 0.15f;

    public override int Execute(CommandContext context, InspectRoomsSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        string[] maps = settings.Maps.Length > 0 ? settings.Maps
            : settings.Levels is not null ? ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName).Where(map => File.Exists(Path.Combine(settings.Levels, map))).ToArray()
            : ReferenceLevelSet.CampaignLevels.Select(ReferenceLevelSet.GetCampaignName).Concat(AddOnLevels).ToArray();
        Directory.CreateDirectory(settings.Out);
        string crops = Path.Combine(settings.Out, "crops");
        if (settings.Crops)
        {
            Directory.CreateDirectory(crops);
        }

        var renderer = new MapRenderer(settings.Game, objects);
        var atlases = new RoomAtlas?[maps.Length];
        var themes = new LevelTheme[maps.Length];
        var lattices = new Lattice?[maps.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        var loader = new LevelLoader(objects);
        AnsiConsole.Status().Start("Reading the rooms", _ => Parallel.For(0, maps.Length, options, mapIndex =>
        {
            string mapPath = Path.Combine(settings.Levels ?? Path.Combine(settings.Game, "Maps"), maps[mapIndex]);
            if (!File.Exists(mapPath))
            {
                return;
            }

            var level = loader.Load(maps[mapIndex], mapPath);
            if (level is null)
            {
                return;
            }

            var survey = level.Survey;
            var atlas = settings.Levels is null ? level.Atlas : RoomAtlas.Read(survey, level.Analysis.Grid.Edges, level.Map.Sprites, objects, isOriginal: false);
            atlases[mapIndex] = atlas;
            themes[mapIndex] = survey.Setting;
            lattices[mapIndex] = survey.Lattice;
            using var rendered = renderer.Render(level.Map.Sprites, settings.Scale, level.Map.Graphics);
            var sheet = new RoomSheet();
            string name = Path.ChangeExtension(maps[mapIndex], null).Replace('/', '_');
            foreach (var room in atlas.Rooms.Where(room => room.IsRoom))
            {
                var tile = sheet.Add(rendered, survey.Lattice, level.Analysis.Grid.Edges, room);
                if (settings.Crops)
                {
                    tile.SaveAsPng(Path.Combine(crops, $"{name}_P{room.Place.Id}.png"));
                }
            }

            sheet.Save(Path.Combine(settings.Out, name + "_rooms"));
        }));

        var rooms = atlases.Where(atlas => atlas is not null).SelectMany(atlas => atlas!.Rooms).ToArray();
        File.WriteAllText(Path.Combine(settings.Out, "rooms.tsv"), WriteRooms(rooms));
        File.WriteAllText(Path.Combine(settings.Out, "rooms.md"), WriteTables(maps, atlases, themes));
        File.WriteAllText(Path.Combine(settings.Out, "recipes.md"), WriteRecipes(atlases, themes, lattices.FirstOrDefault(lattice => lattice is not null)));
        var table = new Table().AddColumns("Type", "Rooms", "Corrected", "Area (k px²)", "Fill", "Roles");
        foreach (var group in rooms.Where(room => room.IsRoom).GroupBy(room => room.Type).OrderBy(group => group.Key))
        {
            int[] areas = group.Select(room => room.Place.AreaPixels / 1000).Order().ToArray();
            table.AddRow(group.Key.ToString(), group.Count().ToString(), group.Count(room => room.Type != room.ProposedType).ToString(),
                $"{areas[0]} / {areas[areas.Length / 2]} / {areas[^1]}", $"{group.Average(room => room.Fill):0.00}", DescribeRoles(group));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Wrote the room sheets to [green]{Markup.Escape(settings.Out)}[/].");
        return atlases.All(atlas => atlas is not null) ? 0 : 1;
    }

    private static string DescribeRoles(IEnumerable<AtlasRoom> rooms) =>
        string.Join(", ", rooms.GroupBy(room => room.Role).OrderByDescending(group => group.Count()).Select(group => $"{group.Key} {group.Count()}"));

    private static string WriteRooms(IEnumerable<AtlasRoom> rooms)
    {
        var text = new StringBuilder("map\tplace\tkind\trole\ttype\tside\tproposed\tzone\tuse\tdoors\tarea\tfill\tdesks\tpieces\n");
        foreach (var room in rooms)
        {
            var place = room.Place;
            text.Append($"{room.MapName}\t{place.Id}\t{place.Kind}\t{room.Role}\t{room.Type}\t{room.Side}\t{room.ProposedType}\t{place.Zone}\t{place.Use}\t{room.Doors}\t{place.AreaPixels}\t{room.Fill:0.00}\t{room.Desks}\t")
                .AppendLine(string.Join(" ", room.Pieces.Select(pair => $"{(int)pair.Key}x{pair.Value}")));
        }

        return text.ToString();
    }

    private static string WriteRecipes(RoomAtlas?[] atlases, LevelTheme[] themes, Lattice? lattice)
    {
        if (lattice is null)
        {
            return "";
        }

        var text = new StringBuilder();
        foreach (var theme in new[] { LevelTheme.Lab, LevelTheme.Military })
        {
            var rooms = atlases.Where((atlas, index) => atlas is not null && themes[index] == theme).SelectMany(atlas => atlas!.Rooms);
            text.AppendLine($"# {theme} recipes\n");
            foreach (var recipe in RoomRecipe.Measure(rooms, lattice))
            {
                (int least, int median, int most) = recipe.Areas;
                text.AppendLine($"## {recipe.Type}: {recipe.Rooms} rooms of {least / 1000}k to {most / 1000}k px² (median {median / 1000}k)\n");
                text.AppendLine("| part | per 100k px² | rooms | wall / corner / center | in rows | pieces (min / median / max) |");
                text.AppendLine("|---|---|---|---|---|---|");
                foreach (var part in recipe.Parts.Where(part => part.RoomShare >= MinListedShare))
                {
                    int[] sizes = part.Examples.Select(example => example.Pieces.Count).Order().ToArray();
                    string kind = part.IsRequired ? $"**{part.Kind}**" : part.Kind;
                    string spots = string.Join(" / ", new[] { PartSpot.Wall, PartSpot.Corner, PartSpot.Center }.Select(spot => $"{part.SpotShares.GetValueOrDefault(spot):P0}"));
                    text.AppendLine($"| {kind} | {part.Rate:0.00} | {part.RoomShare:P0} | {spots} | {part.RowShare:P0} | {sizes[0]} / {sizes[sizes.Length / 2]} / {sizes[^1]} |");
                }

                text.AppendLine($"\nScattered per 100k px²: {string.Join(", ", recipe.ScatterRates.Select(pair => $"{(int)pair.Key} {pair.Value:0.00}"))}\n");
            }
        }

        return text.ToString();
    }

    private static string WriteTables(string[] maps, RoomAtlas?[] atlases, LevelTheme[] themes)
    {
        var text = new StringBuilder();
        foreach (var theme in new[] { LevelTheme.Lab, LevelTheme.Military })
        {
            int[] levels = Enumerable.Range(0, maps.Length).Where(index => atlases[index] is not null && themes[index] == theme).ToArray();
            var rooms = levels.SelectMany(index => atlases[index]!.Rooms).Where(room => room.IsRoom).ToArray();
            var types = rooms.Select(room => room.Type).Distinct().Order().ToArray();
            text.AppendLine($"## {theme} rooms by role and type\n");
            text.AppendLine($"| role | {string.Join(" | ", types)} |");
            text.AppendLine($"|---|{string.Concat(types.Select(_ => "---|"))}");
            foreach (var role in rooms.Select(room => room.Role).Distinct().Order())
            {
                text.AppendLine($"| {role} | {string.Join(" | ", types.Select(type => rooms.Count(room => room.Role == role && room.Type == type)))} |");
            }

            text.AppendLine($"\n## {theme} rooms by level and type\n");
            text.AppendLine($"| level | {string.Join(" | ", types)} |");
            text.AppendLine($"|---|{string.Concat(types.Select(_ => "---|"))}");
            foreach (int index in levels)
            {
                var own = atlases[index]!.Rooms;
                text.AppendLine($"| {maps[index]} | {string.Join(" | ", types.Select(type => own.Count(room => room.IsRoom && room.Type == type)))} |");
            }

            var split = levels.SelectMany(index => atlases[index]!.Rooms).Where(room => room.Side is not null).ToArray();
            if (split.Length > 0)
            {
                text.AppendLine($"\nSplit halls: {string.Join(", ", split.Select(room => $"{room.MapName} P{room.Place.Id} ({room.Side} side)"))}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }
}

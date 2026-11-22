using System.Globalization;
using System.Text;
using ASToolkit.Cli.Analysis;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Bot;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Bot.Rendering;
using ASToolkit.Core.Rooms;
using SixLabors.ImageSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectRoomsCommand : LevelCommand<InspectRoomsSettings>
{
    public override int Execute(CommandContext context, InspectRoomsSettings settings, CancellationToken cancellationToken)
    {
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        string[] maps = GetMaps(settings.Maps, settings.Levels);
        Directory.CreateDirectory(settings.Out);
        string crops = Path.Combine(settings.Out, "crops");
        if (settings.Crops)
        {
            Directory.CreateDirectory(crops);
        }

        var atlases = new RoomAtlas?[maps.Length];
        var wallSets = new WallSet[maps.Length];
        var loader = new LevelLoader(objects);
        AnsiConsole.Status().Start("Reading the rooms", _ => Parallel.For(0, maps.Length, CreateParallelOptions(cancellationToken), mapIndex =>
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
            wallSets[mapIndex] = survey.FloorSet;
            using var rendered = MapImage.Render(Playthrough.LoadMap(settings.Game, new SpriteCatalog(settings.Game, objects), mapPath), settings.Scale);
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
        File.WriteAllText(Path.Combine(settings.Out, "rooms.md"), WriteTables(maps, atlases, wallSets));
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
            text.Append(CultureInfo.InvariantCulture, $"{room.MapName}\t{place.Id}\t{place.Kind}\t{room.Role}\t{room.Type}\t{room.Side}\t{room.ProposedType}\t{place.Zone}\t{place.Use}\t{room.Doors}\t{place.AreaPixels}\t{room.Fill:0.00}\t{room.Desks}\t")
                .AppendLine(string.Join(" ", room.Pieces.Select(pair => $"{(int)pair.Key}x{pair.Value}")));
        }

        return text.ToString();
    }

    private static string WriteTables(string[] maps, RoomAtlas?[] atlases, WallSet[] wallSets)
    {
        var text = new StringBuilder();
        foreach (var wallSet in new[] { WallSet.Basement, WallSet.Concrete })
        {
            int[] levels = Enumerable.Range(0, maps.Length).Where(index => atlases[index] is not null && wallSets[index] == wallSet).ToArray();
            var rooms = levels.SelectMany(index => atlases[index]!.Rooms).Where(room => room.IsRoom).ToArray();
            var types = rooms.Select(room => room.Type).Distinct().Order().ToArray();
            text.AppendLine($"## {wallSet} rooms by role and type\n");
            text.AppendLine($"| role | {string.Join(" | ", types)} |");
            text.AppendLine($"|---|{string.Concat(types.Select(_ => "---|"))}");
            foreach (var role in rooms.Select(room => room.Role).Distinct().Order())
            {
                text.AppendLine($"| {role} | {string.Join(" | ", types.Select(type => rooms.Count(room => room.Role == role && room.Type == type)))} |");
            }

            text.AppendLine($"\n## {wallSet} rooms by level and type\n");
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

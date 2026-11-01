using ASToolkit.Core.Levels;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Rooms;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Drawing;

namespace ASToolkit.Cli.Analysis;

internal sealed class RoomSheet
{
    private const int PageWidth = 1800;
    private const int PageHeight = 1800;
    private const int MaxTileSide = 640;
    private const int MinTileWidth = 260;
    private const int Gap = 10;
    private const int Margin = 24;
    private const int TopMargin = 60;
    private const float FontSize = 14;
    private const int LabelLines = 3;

    private static readonly DrawingOptions FloorTint = new() { GraphicsOptions = new GraphicsOptions { BlendPercentage = 0.15f } };
    private static readonly Lazy<FontFamily> LabelFamily = new(() => SystemFonts.Families.OrderBy(family => family.Name).First());
    private static readonly PngEncoder PngOptions = new() { CompressionLevel = PngCompressionLevel.BestSpeed };

    private readonly List<Image<Rgb24>> _tiles = [];
    private readonly Font _font = LabelFamily.Value.CreateFont(FontSize, FontStyle.Bold);

    public Image<Rgb24> Add(MapImage map, Lattice lattice, IReadOnlyDictionary<EdgeKey, char> edges, AtlasRoom room)
    {
        var corners = room.Place.Cells.SelectMany(cell => new[] { (cell.U, cell.V), (cell.U + 1, cell.V), (cell.U, cell.V + 1), (cell.U + 1, cell.V + 1) })
            .Select(corner => ToImage(map, lattice, corner.Item1, corner.Item2)).ToArray();
        var box = Rectangle.FromLTRB((int)corners.Min(point => point.X) - Margin, (int)corners.Min(point => point.Y) - TopMargin,
            (int)corners.Max(point => point.X) + Margin, (int)corners.Max(point => point.Y) + Margin);
        box.Intersect(map.Image.Bounds);
        var crop = map.Image.Clone(context => context.Crop(box));
        var cells = room.Place.Cells.ToHashSet();
        crop.Mutate(context =>
        {
            var floor = cells.Select(IPath (cell) => new Polygon(new LinearLineSegment(
                new[] { (cell.U, cell.V), (cell.U + 1, cell.V), (cell.U + 1, cell.V + 1), (cell.U, cell.V + 1) }
                    .Select(corner => ToImage(map, lattice, corner.Item1, corner.Item2)).Select(point => new PointF(point.X - box.X, point.Y - box.Y)).ToArray())));
            context.Fill(FloorTint, Color.Yellow, new PathCollection(floor));

            foreach (var cell in cells)
            {
                foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                {
                    bool inside = cells.Contains(neighbor);
                    if (inside && !(edges.TryGetValue(edge, out char kind) && kind is 'W' or 'F'))
                    {
                        continue;
                    }

                    var (start, end) = edge.Axis == 'v' ? ((edge.A, edge.B), (edge.A + 1, edge.B)) : ((edge.A, edge.B), (edge.A, edge.B + 1));
                    var from = ToImage(map, lattice, start.Item1, start.Item2);
                    var to = ToImage(map, lattice, end.Item1, end.Item2);
                    context.DrawLine(inside ? Color.Orange : Color.Yellow, 2, new PointF(from.X - box.X, from.Y - box.Y), new PointF(to.X - box.X, to.Y - box.Y));
                }
            }
        });

        float shrink = Math.Min(1, MaxTileSide / (float)Math.Max(crop.Width, crop.Height));
        if (shrink < 1)
        {
            var size = new Size((int)(crop.Width * shrink), (int)(crop.Height * shrink));
            crop.Mutate(context => context.Resize(size));
        }

        const int labelHeight = (int)(LabelLines * FontSize * 1.3f) + 6;
        var tile = new Image<Rgb24>(Math.Max(MinTileWidth, crop.Width), crop.Height + labelHeight, new Rgb24(24, 24, 24));
        string[] lines = Describe(room);
        tile.Mutate(new DrawImageProcessor(crop, new Point(0, labelHeight), PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
        tile.Mutate(context =>
        {
            for (int index = 0; index < lines.Length; index++)
            {
                context.DrawText(lines[index], _font, index == 0 ? Color.White : Color.LightGray, new PointF(4, 3 + index * FontSize * 1.3f));
            }
        });
        crop.Dispose();
        _tiles.Add(tile);
        return tile;
    }

    public int Save(string prefix)
    {
        var pages = new List<List<(Image<Rgb24> Tile, Point At)>>();
        var page = new List<(Image<Rgb24>, Point)>();
        int x = 0, y = 0, rowHeight = 0, pageHeight = 0;
        foreach (var tile in _tiles)
        {
            if (x > 0 && x + tile.Width > PageWidth)
            {
                (x, y, rowHeight) = (0, y + rowHeight + Gap, 0);
            }

            if (y > 0 && y + tile.Height > PageHeight)
            {
                pages.Add(page);
                (page, x, y, rowHeight) = ([], 0, 0, 0);
            }

            page.Add((tile, new Point(x, y)));
            x += tile.Width + Gap;
            rowHeight = Math.Max(rowHeight, tile.Height);
            pageHeight = Math.Max(pageHeight, y + rowHeight);
        }

        if (page.Count > 0)
        {
            pages.Add(page);
        }

        for (int index = 0; index < pages.Count; index++)
        {
            int width = pages[index].Max(entry => entry.At.X + entry.Tile.Width);
            int height = pages[index].Max(entry => entry.At.Y + entry.Tile.Height);
            using var sheet = new Image<Rgb24>(width, height, new Rgb24(60, 60, 60));
            var entries = pages[index];
            sheet.Mutate(context =>
            {
                foreach (var (tile, at) in entries)
                {
                    context.DrawImage(tile, at, 1);
                }
            });
            sheet.SaveAsPng($"{prefix}_{index + 1}.png", PngOptions);
        }

        foreach (var tile in _tiles)
        {
            tile.Dispose();
        }

        _tiles.Clear();
        return pages.Count;
    }

    private static PointF ToImage(MapImage map, Lattice lattice, float u, float v)
    {
        var world = lattice.ToWorld(u, v);
        return map.ToImage(world.X, world.Y);
    }

    private static string[] Describe(AtlasRoom room)
    {
        var place = room.Place;
        string type = (room.Side is null ? room.Type.ToString() : $"{room.Type} + {room.Side} side") + (room.Type == room.ProposedType ? "" : $" (classifier: {room.ProposedType})");
        string use = place.Use == Core.Survey.PlaceUse.None ? "" : $" {place.Use}";
        string pieces = string.Join(" ", room.Pieces.OrderByDescending(pair => pair.Value).Take(6).Select(pair => $"{(int)pair.Key}x{pair.Value}"));
        return
        [
            $"P{place.Id} {room.Role} - {type}",
            $"{place.Kind} {place.Zone}{use} d{room.Doors} {place.AreaPixels / 1000}k f{room.Fill:0.00} desks {room.Desks}",
            pieces.Length == 0 ? "empty" : pieces
        ];
    }
}

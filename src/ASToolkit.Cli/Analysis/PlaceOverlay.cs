using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASToolkit.Cli.Analysis;

internal static class PlaceOverlay
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(230, 25, 75), Color.FromRgb(60, 180, 75), Color.FromRgb(255, 225, 25), Color.FromRgb(0, 130, 200), Color.FromRgb(245, 130, 48),
        Color.FromRgb(145, 30, 180), Color.FromRgb(70, 240, 240), Color.FromRgb(240, 50, 230), Color.FromRgb(210, 245, 60), Color.FromRgb(250, 190, 212),
        Color.FromRgb(0, 128, 128), Color.FromRgb(220, 190, 255), Color.FromRgb(170, 110, 40), Color.FromRgb(128, 0, 0), Color.FromRgb(170, 255, 195)
    ];

    private static readonly Dictionary<ConnectionKind, Color> ConnectionColors = new()
    {
        [ConnectionKind.Opening] = Color.White,
        [ConnectionKind.Doorway] = Color.FromRgb(0, 255, 255),
        [ConnectionKind.Door] = Color.FromRgb(0, 255, 0),
        [ConnectionKind.LockedDoor] = Color.FromRgb(255, 0, 255),
        [ConnectionKind.JammedDoor] = Color.FromRgb(128, 128, 128),
        [ConnectionKind.SecretWall] = Color.FromRgb(255, 255, 0),
        [ConnectionKind.SpawnWall] = Color.FromRgb(255, 128, 0),
        [ConnectionKind.MonsterHole] = Color.FromRgb(255, 0, 0),
        [ConnectionKind.BrokenWall] = Color.FromRgb(200, 200, 100),
        [ConnectionKind.FenceFrame] = Color.FromRgb(100, 200, 255),
        [ConnectionKind.Fence] = Color.FromRgb(0, 120, 255)
    };

    private const float LegendFontSize = 16;
    private const float LegendMargin = 8;
    private const float LegendRow = 24;
    private const float LegendSwatch = 28;

    private static readonly Lazy<FontFamily> LabelFamily = new(() => SystemFonts.Families.OrderBy(family => family.Name).First());

    public static void Draw(MapImage map, LevelSurvey survey, RouteAnalysis analysis)
    {
        var lattice = survey.Lattice;
        var font = LabelFamily.Value.CreateFont(Math.Max(10, 26 * map.Scale), FontStyle.Bold);

        var sectionOf = new SectionMeasurer(survey).Measure()
            .SelectMany(section => section.Places.Select(place => (Place: place, section.Index)))
            .ToDictionary(entry => entry.Place, entry => entry.Index);

        map.Image.Mutate<Rgb24>(context =>
        {
            foreach (var place in survey.Places)
            {
                var color = (sectionOf.TryGetValue(place.Id, out int section) ? Palette[section % Palette.Length] : Color.Gray).WithAlpha(IsOffRoute(place.Id) ? 0.12f : 0.22f);
                var placeCells = place.Cells;
                var cells = placeCells.Select(IPath (cell) => new Polygon(new LinearLineSegment(Corner(cell.U, cell.V), Corner(cell.U + 1, cell.V), Corner(cell.U + 1, cell.V + 1),
                    Corner(cell.U, cell.V + 1)))).ToArray();
                context.Fill(color, new PathCollection(cells));
                var set = placeCells.ToHashSet();
                foreach (var cell in placeCells)
                {
                    foreach (var (neighbor, edge) in EdgeKey.GetNeighbors(cell))
                    {
                        if (!set.Contains(neighbor))
                        {
                            (float from, float to) = edge.Axis == 'v' ? ((float)edge.A, (float)edge.A + 1) : (edge.B, edge.B + 1);
                            var start = edge.Axis == 'v' ? Corner(from, edge.B) : Corner(edge.A, from);
                            var end = edge.Axis == 'v' ? Corner(to, edge.B) : Corner(edge.A, to);
                            context.DrawLine(Color.Black, Math.Max(1, 3 * map.Scale), start, end);
                        }
                    }
                }
            }

            var runs = analysis.GetTourRuns();
            foreach (var run in runs)
            {
                context.DrawLine(Color.White.WithAlpha(0.7f), Math.Max(1, 3 * map.Scale), run.Points.Select(point => map.ToImage(point.X, point.Y)).ToArray());
            }

            foreach (var connection in survey.Connections)
            {
                var middles = connection.Edges.SelectMany(run => run.GetEdges()).Select(edge => edge.Axis == 'v' ? lattice.ToWorld(edge.A + 0.5f, edge.B) : lattice.ToWorld(edge.A, edge.B + 0.5f)).ToArray();
                var at = map.ToImage(middles.Average(middle => middle.X), middles.Average(middle => middle.Y));
                context.Fill(ConnectionColors[connection.Kind], new EllipsePolygon(at, Math.Max(3, 9 * map.Scale)));
            }

            foreach (var place in survey.Places)
            {
                string label = sectionOf.TryGetValue(place.Id, out int section) ? $"{place.Id}/{section + 1}" : place.Id.ToString();
                var at = map.ToImage(place.Center.X, place.Center.Y);
                var bounds = TextMeasurer.MeasureBounds(label, new TextOptions(font));
                context.Fill(Color.Black.WithAlpha(0.75f), new RectangularPolygon(at.X - bounds.Width / 2 - 3, at.Y - bounds.Height / 2 - 3, bounds.Width + 6, bounds.Height + 6));
                context.DrawText(label, font, IsOffRoute(place.Id) ? Color.Orange : Color.White, new PointF(at.X - bounds.Width / 2 - bounds.X, at.Y - bounds.Height / 2 - bounds.Y));
            }
        });

        var content = RouteOverlay.FindContent(map.Image);
        if (content is not null)
        {
            var area = Rectangle.Intersect(Rectangle.Inflate(content.Value, 10, 10), map.Image.Bounds);
            map.Image.Mutate(context => context.Crop(area));
        }

        DrawLegend(map, survey);
        return;

        bool IsOffRoute(int place) => survey.Places[place - 1].FirstVisit is null;

        PointF Corner(float u, float v)
        {
            var world = lattice.ToWorld(u, v);
            return map.ToImage(world.X, world.Y);
        }
    }

    private static void DrawLegend(MapImage map, LevelSurvey survey)
    {
        var font = LabelFamily.Value.CreateFont(LegendFontSize, FontStyle.Bold);
        var rows = new List<(Color Color, char Shape, string Text)>
        {
            (Color.White, 'n', "place/section on the route"),
            (Color.Orange, 'n', "place the route never enters"),
            (Color.White, '-', "the tour")
        };
        rows.AddRange(survey.Connections.Select(connection => connection.Kind).Distinct().Order().Select(kind => (ConnectionColors[kind], 'o', DescribeKind(kind))));
        float width = rows.Max(row => TextMeasurer.MeasureBounds(row.Text, new TextOptions(font)).Width) + 2 * LegendMargin + LegendSwatch;
        float height = rows.Count * LegendRow + LegendMargin;
        map.Image.Mutate(context =>
        {
            context.Fill(Color.Black.WithAlpha(0.8f), new RectangularPolygon(LegendMargin, LegendMargin, width, height));
            for (int index = 0; index < rows.Count; index++)
            {
                (var color, char shape, string text) = rows[index];
                float top = LegendMargin + LegendMargin / 2 + index * LegendRow;
                var middle = new PointF(LegendMargin * 2 + LegendSwatch / 2, top + LegendRow / 2);
                switch (shape)
                {
                    case 'n':
                        context.DrawText("7", font, color, new PointF(middle.X - LegendFontSize / 4, top));
                        break;
                    case '-':
                        context.DrawLine(color, 3, new PointF(middle.X - LegendSwatch / 2, middle.Y), new PointF(middle.X + LegendSwatch / 2, middle.Y));
                        break;
                    default:
                        context.Fill(color, new EllipsePolygon(middle, LegendSwatch / 3));
                        break;
                }

                context.DrawText(text, font, Color.White, new PointF(LegendMargin * 2 + LegendSwatch + LegendMargin, top));
            }
        });
    }

    private static string DescribeKind(ConnectionKind kind) => kind switch
    {
        ConnectionKind.LockedDoor => "locked door",
        ConnectionKind.JammedDoor => "jammed door",
        ConnectionKind.SecretWall => "breakable wall",
        ConnectionKind.SpawnWall => "spawn wall",
        ConnectionKind.MonsterHole => "monster hole",
        ConnectionKind.BrokenWall => "broken wall",
        ConnectionKind.FenceFrame => "fence frame",
        _ => kind.ToString().ToLowerInvariant()
    };
}

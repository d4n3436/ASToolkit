using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ASToolkit.Cli.Analysis;

// Draws a level's tour, elements and objectives over its map and crops the image to the level
internal static class RouteOverlay
{
    // Marker and line sizes are tuned for a quarter-scale map and grow with the scale
    private const float TunedScale = 0.25f;
    private const int ContentThreshold = 12;

    private static readonly Color Start = Color.FromRgb(0, 255, 0);
    private static readonly Color End = Color.FromRgb(255, 0, 0);
    private static readonly Color Checkpoint = Color.FromRgb(255, 255, 255);
    private static readonly Color Blackout = Color.FromRgb(180, 0, 255);
    private static readonly Color Trigger = Color.FromRgb(255, 170, 0);
    private static readonly Color LabelBack = Color.FromRgb(0, 0, 0);
    private static readonly Color LabelText = Color.FromRgb(255, 255, 255);

    private static readonly Dictionary<RoutePhase, Color> PhaseColors = new()
    {
        [RoutePhase.Free] = Color.FromRgb(0, 255, 255),
        [RoutePhase.DoorsLocked] = Color.FromRgb(200, 80, 255),
        [RoutePhase.FencesUp] = Color.FromRgb(255, 220, 0)
    };

    private static readonly Dictionary<ElementKind, Color> ElementColors = new()
    {
        [ElementKind.Monster] = Color.FromRgb(255, 80, 60),
        [ElementKind.Boss] = Color.FromRgb(255, 0, 255),
        [ElementKind.Spawner] = Color.FromRgb(255, 170, 0),
        [ElementKind.EndlessGenerator] = Color.FromRgb(255, 120, 0),
        [ElementKind.TeleportPad] = Color.FromRgb(80, 140, 255),
        [ElementKind.DynamiteSite] = Color.FromRgb(255, 40, 40),
        [ElementKind.Breaker] = Color.FromRgb(255, 255, 0),
        [ElementKind.Generator] = Color.FromRgb(0, 255, 255),
        [ElementKind.Fence] = Color.FromRgb(0, 200, 255),
        [ElementKind.MountedCannon] = Color.FromRgb(255, 255, 255),
        [ElementKind.SecretWall] = Color.FromRgb(160, 255, 160),
        [ElementKind.SpawnWall] = Color.FromRgb(255, 200, 120),
        [ElementKind.MonsterHole] = Color.FromRgb(255, 200, 120),
        [ElementKind.PowerTrap] = Color.FromRgb(255, 255, 120),
        [ElementKind.Crate] = Color.FromRgb(170, 120, 60),
        [ElementKind.Pickup] = Color.FromRgb(80, 255, 80)
    };

    private static readonly Dictionary<ObjectiveKind, char> ObjectiveLetters = new()
    {
        [ObjectiveKind.DynamiteSite] = 'S',
        [ObjectiveKind.Breaker] = 'B',
        [ObjectiveKind.Generator] = 'G',
        [ObjectiveKind.Dynamite] = 'D'
    };

    private static readonly Lazy<FontFamily> LabelFamily = new(() => SystemFonts.Families.OrderBy(f => f.Name).First(HasLabelGlyphs));

    public static void Draw(MapImage map, RouteAnalysis analysis, LevelScriptSummary script)
    {
        float k = (float)(map.Scale / TunedScale);
        var tour = analysis.Tour
            .Select(c => analysis.Grid.Lattice.GetCenter(c))
            .Select(p => map.ToImage(p.X, p.Y))
            .ToArray();
        
        map.Image.Mutate(c =>
        {
            foreach (var run in analysis.GetTourRuns())
            {
                c.DrawLine(PhaseColors[run.Phase], Size(3), run.Points.Select(p => map.ToImage(p.X, p.Y)).ToArray());
            }

            foreach (var element in analysis.Elements.Where(e => e.Program?.Trigger is not null))
            {
                var t = element.Program!.Trigger!.Value;
                var trigger = map.ToImage(t.X, t.Y);
                c.DrawLine(Trigger, Size(1), map.ToImage(element.X, element.Y), trigger);
                if (t.Radius > 0)
                {
                    c.Draw(Trigger, Size(1), new EllipsePolygon(trigger, (float)(t.Radius * map.Scale)));
                }
            }

            foreach (var element in analysis.Elements.Where(e => e.Kind != ElementKind.Crate || e.CrateItem != 0))
            {
                var dot = new EllipsePolygon(map.ToImage(element.X, element.Y), Size(element.Kind is ElementKind.Monster or ElementKind.Pickup or ElementKind.Crate ? 2 : 4));
                var color = ElementColors.GetValueOrDefault(element.Kind, Color.White);
                if (element.Hidden)
                {
                    c.Draw(color, Size(2), dot);
                }
                else
                {
                    c.Fill(color, dot);
                }
            }

            foreach (var checkpoint in analysis.Checkpoints)
            {
                c.Draw(Checkpoint, Size(2), Square(map.ToImage(checkpoint.X, checkpoint.Y), Size(5)));
            }

            if (script.Blackout is not null)
            {
                c.Draw(Blackout, Size(3), Square(map.ToImage(script.Blackout.Value.Center.X, script.Blackout.Value.Center.Y), Size(6)));
            }

            DrawObjectiveLabels(c, map, analysis.Objectives, Size, LabelFamily.Value.CreateFont(Math.Max(10, MathF.Round(11 * k)), FontStyle.Bold));

            if (tour.Length > 0)
            {
                c.Draw(Start, Size(3), new EllipsePolygon(tour[0], Size(8)));
                var last = analysis.Objectives.MaxBy(o => o.Progress);
                c.Draw(End, Size(3), new EllipsePolygon(last is null ? tour[0] : map.ToImage(last.X, last.Y), Size(8)));
            }
        });

        var content = FindContent(map.Image);
        if (content is not null)
        {
            int margin = (int)Size(10);
            var area = Rectangle.Intersect(Rectangle.Inflate(content.Value, margin, margin), map.Image.Bounds);
            map.Image.Mutate(c => c.Crop(area));
        }

        return;

        float Size(float w) => Math.Max(1, MathF.Round((float)w * k));
    }

    // Number sites, generators and dynamite in tour order and label the breaker B
    private static void DrawObjectiveLabels(IImageProcessingContext c, MapImage map, IReadOnlyList<RouteObjective> objectives, Func<float, float> size, Font font)
    {
        var counts = new Dictionary<char, int>();
        foreach (var objective in objectives.Where(o => ObjectiveLetters.ContainsKey(o.Kind)).OrderBy(o => o.Progress))
        {
            char letter = ObjectiveLetters[objective.Kind];
            counts[letter] = counts.GetValueOrDefault(letter) + 1;
            string text = letter == 'B' ? "B" : $"{letter}{counts[letter]}";
            var at = map.ToImage(objective.X, objective.Y);
            var bounds = TextMeasurer.MeasureBounds(text, new TextOptions(font));
            float pad = size(2);
            float left = at.X + size(5);
            float top = at.Y - size(5) - bounds.Height - 2 * pad;
            c.Fill(LabelBack, new RectangularPolygon(left, top, bounds.Width + 2 * pad, bounds.Height + 2 * pad));
            c.DrawText(text, font, LabelText, new PointF(left + pad - bounds.X, top + pad - bounds.Y));
        }
    }

    // Bounding box of the pixels that differ from the background by more than the threshold
    private static Rectangle? FindContent(Image<Rgb24> image)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        var b = MapRenderer.Background;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (Math.Abs(row[x].R - b.R) > ContentThreshold || Math.Abs(row[x].G - b.G) > ContentThreshold || Math.Abs(row[x].B - b.B) > ContentThreshold)
                    {
                        (x0, x1, y0, y1) = (Math.Min(x0, x), Math.Max(x1, x), Math.Min(y0, y), Math.Max(y1, y));
                    }
                }
            }
        });
        return x1 < 0 ? null : new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    private static RectangularPolygon Square(PointF center, float half) => new(center.X - half, center.Y - half, 2 * half, 2 * half);

    private static bool HasLabelGlyphs(FontFamily family) =>
        family.TryGetMetrics(FontStyle.Bold, out var metrics)
        && "SGBD0123456789".All(c => metrics.TryGetGlyphMetrics(new CodePoint(c), TextAttributes.None, TextDecorations.None, LayoutMode.HorizontalTopBottom, ColorFontSupport.None, out _));
}

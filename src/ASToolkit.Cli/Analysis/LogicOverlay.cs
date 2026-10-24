using System.Numerics;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace ASToolkit.Cli.Analysis;

// Draws the script events, the quest steps and the walk of a level over its map
internal static class LogicOverlay
{
    // Marker and line sizes are tuned for a quarter-scale map and grow with the scale
    private const float TunedScale = 0.25f;

    private static readonly Color Walked = Color.FromRgb(0, 255, 255);
    private static readonly Color Teleport = Color.FromRgb(80, 140, 255);
    private static readonly Color Controlled = Color.FromRgb(255, 80, 255);
    private static readonly Color Unreached = Color.FromRgb(255, 60, 60);
    private static readonly Color LabelBack = Color.FromRgb(0, 0, 0);
    private static readonly Color LabelText = Color.FromRgb(255, 255, 255);

    private static readonly Dictionary<ScriptMarkerKind, Color> MarkerColors = new()
    {
        [ScriptMarkerKind.Other] = Color.FromRgba(170, 170, 170, 110),
        [ScriptMarkerKind.Dialog] = Color.FromRgb(120, 200, 255),
        [ScriptMarkerKind.Spawn] = Color.FromRgb(255, 140, 0),
        [ScriptMarkerKind.Opening] = Color.FromRgb(255, 230, 0),
        [ScriptMarkerKind.Closing] = Color.FromRgb(255, 90, 160),
        [ScriptMarkerKind.Checkpoint] = Color.FromRgb(255, 255, 255),
        [ScriptMarkerKind.Control] = Color.FromRgb(255, 80, 255),
        [ScriptMarkerKind.Teleport] = Color.FromRgb(80, 140, 255),
        [ScriptMarkerKind.Blackout] = Color.FromRgb(180, 0, 255),
        [ScriptMarkerKind.Failure] = Color.FromRgb(255, 0, 0),
        [ScriptMarkerKind.Quest] = Color.FromRgb(0, 255, 160),
        [ScriptMarkerKind.Exit] = Color.FromRgb(255, 60, 60)
    };

    private static readonly Lazy<FontFamily> LabelFamily = new(() => SystemFonts.Families.OrderBy(family => family.Name).First());

    public static void Draw(MapImage map, LevelLogic logic, WalkedRoute? walk)
    {
        float k = (float)(map.Scale / TunedScale);
        var font = LabelFamily.Value.CreateFont(Math.Max(10, MathF.Round(11 * k)), FontStyle.Bold);
        map.Image.Mutate(context =>
        {
            foreach (var marker in logic.Markers.Where(marker => marker.Kind != ScriptMarkerKind.Other).OrderBy(marker => marker.Kind))
            {
                var color = MarkerColors[marker.Kind];
                var center = ToImage(map, marker.Position);
                if (marker.Radius > 0)
                {
                    context.Draw(color, Size(1), new EllipsePolygon(center, Math.Max(Size(3), (float)(marker.Radius * map.Scale))));
                }
                else
                {
                    context.Fill(color, new EllipsePolygon(center, Size(3)));
                }
            }

            foreach (var segment in walk?.Segments ?? [])
            {
                var points = segment.Points.Select(point => ToImage(map, point)).ToArray();
                if (points.Length < 2)
                {
                    continue;
                }

                Pen pen = segment.Kind switch
                {
                    RouteSegmentKind.Walked => Pens.Solid(Walked, Size(3)),
                    RouteSegmentKind.Controlled => Pens.Solid(Controlled, Size(3)),
                    RouteSegmentKind.Teleport => Pens.Dash(Teleport, Size(2)),
                    _ => Pens.Dash(Unreached, Size(2))
                };
                context.DrawLine(pen, points);
            }

            var steps = walk?.Steps ?? logic.Progression;
            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                var at = ToImage(map, steps[stepIndex].Position);
                string text = (stepIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var bounds = TextMeasurer.MeasureBounds(text, new TextOptions(font));
                float pad = Size(2);
                float left = at.X + Size(5);
                float top = at.Y - Size(5) - bounds.Height - 2 * pad;
                context.Fill(LabelBack, new RectangularPolygon(left, top, bounds.Width + 2 * pad, bounds.Height + 2 * pad));
                context.DrawText(text, font, LabelText, new PointF(left + pad - bounds.X, top + pad - bounds.Y));
                context.Draw(Walked, Size(2), new EllipsePolygon(at, Size(5)));
            }
        });

        return;

        float Size(float width) => Math.Max(1, MathF.Round((float)width * k));
    }

    private static PointF ToImage(MapImage map, Vector3 point) => map.ToImage(point.X, point.Y, point.Z);
}

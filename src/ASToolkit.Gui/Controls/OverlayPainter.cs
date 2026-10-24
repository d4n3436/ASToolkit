using System.Globalization;
using System.Numerics;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Gui.Models;
using Avalonia;
using Avalonia.Media;

namespace ASToolkit.Gui.Controls;

internal sealed class OverlayPainter
{
    private const double HelperRadius = 5;
    private const double BlockerRadius = 3;
    private const double PointMarkerSize = 4;
    private const double SmallDotRadius = 2.5;
    private const double LargeDotRadius = 4;
    private const double EndRadius = 9;
    private const double LabelSize = 12;
    private const double MinSourceDistance = 2;
    private const byte AreaFillAlpha = 28;
    private const byte FaintAlpha = 110;

    private static readonly Color TeleportColor = Color.FromRgb(80, 140, 255);
    private static readonly Color ControlledColor = Color.FromRgb(255, 80, 255);
    private static readonly Color UnreachedColor = Color.FromRgb(255, 60, 60);
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
    private static readonly DashStyle Dashes = new([4, 3], 0);

    private static readonly Dictionary<HelperKind, Color> HelperColors = new()
    {
        [HelperKind.Other] = Color.FromRgb(255, 0, 255),
        [HelperKind.Missing] = Color.FromRgb(255, 255, 255),
        [HelperKind.Blocker] = Color.FromRgb(200, 200, 200),
        [HelperKind.Trigger] = Color.FromRgb(80, 160, 255),
        [HelperKind.Spawner] = Color.FromRgb(255, 60, 60)
    };

    private static readonly Dictionary<ScriptMarkerKind, Color> MarkerColors = new()
    {
        [ScriptMarkerKind.Other] = Color.FromRgb(170, 170, 170),
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

    private readonly DrawingContext _context;
    private readonly Func<Vector2, Point> _toView;
    private readonly Rect _view;
    private readonly double _zoom;

    public OverlayPainter(DrawingContext context, Func<Vector2, Point> toView, Rect view, double zoom)
    {
        _context = context;
        _toView = toView;
        _view = view;
        _zoom = zoom;
    }

    public void DrawRoute(MapOverlay overlay)
    {
        if (overlay.Route is not null)
        {
            DrawTour(overlay.Route, overlay.TourRuns);
        }

        if (overlay.Walk is not null)
        {
            DrawWalk(overlay.Walk);
        }

        DrawProgression(overlay.Walk?.Steps ?? overlay.Logic.Progression, overlay.Walk is null);
    }

    public void DrawMarkers(IReadOnlyList<ScriptMarker> markers)
    {
        foreach (var marker in markers.OrderBy(marker => marker.Kind))
        {
            var color = MarkerColors[marker.Kind];
            byte alpha = marker.Kind == ScriptMarkerKind.Other ? FaintAlpha : (byte)230;
            var pen = new Pen(new SolidColorBrush(color, alpha / 255.0), 1.5);
            var center = ToView(marker.Position);
            if (Vector3.Distance(marker.Source, marker.Position) > MinSourceDistance)
            {
                DrawLine(new Pen(pen.Brush, 1), ToView(marker.Source), center);
            }

            if (marker.Target is not null)
            {
                var target = ToView(marker.Target.Value);
                DrawLine(new Pen(pen.Brush, 1.5, Dashes), center, target);
                DrawCircle(null, pen, target, PointMarkerSize);
            }

            if (marker.Radius > 0)
            {
                DrawCircle(new SolidColorBrush(color, AreaFillAlpha / 255.0), pen, center, Math.Max(PointMarkerSize, marker.Radius * _zoom));
            }
            else if (IsVisible(center, PointMarkerSize))
            {
                _context.DrawRectangle(new SolidColorBrush(color, alpha / 255.0), null,
                    new Rect(center.X - PointMarkerSize, center.Y - PointMarkerSize, 2 * PointMarkerSize, 2 * PointMarkerSize));
            }
        }
    }

    public void DrawHelpers(IReadOnlyList<HelperMarker> helpers)
    {
        foreach (var helper in helpers)
        {
            var color = HelperColors[helper.Kind];
            var position = helper.Sprite.Position;
            bool blocker = helper.Kind == HelperKind.Blocker;
            DrawCircle(new SolidColorBrush(color, 90 / 255.0), new Pen(new SolidColorBrush(color, blocker ? FaintAlpha / 255.0 : 1), blocker ? 1 : 2),
                _toView(new Vector2(position.X, position.Y - position.Z)), blocker ? BlockerRadius : HelperRadius);
        }
    }

    private void DrawTour(RouteAnalysis route, IReadOnlyList<TourRun> runs)
    {
        foreach (var run in runs)
        {
            DrawPolyline(new Pen(new SolidColorBrush(PhaseColors[run.Phase]), 3), run.Points);
        }

        foreach (var element in route.Elements.Where(element => element.Kind != ElementKind.Crate || element.CrateItem != 0))
        {
            var color = ElementColors.GetValueOrDefault(element.Kind, Colors.White);
            double radius = element.Kind is ElementKind.Monster or ElementKind.Pickup or ElementKind.Crate ? SmallDotRadius : LargeDotRadius;
            var center = _toView(new Vector2(element.X, element.Y));
            if (element.Hidden)
            {
                DrawCircle(null, new Pen(new SolidColorBrush(color), 1.5), center, radius);
            }
            else
            {
                DrawCircle(new SolidColorBrush(color), null, center, radius);
            }
        }

        var counts = new Dictionary<char, int>();
        foreach (var objective in route.Objectives.Where(objective => ObjectiveLetters.ContainsKey(objective.Kind)).OrderBy(objective => objective.Progress))
        {
            char letter = ObjectiveLetters[objective.Kind];
            counts[letter] = counts.GetValueOrDefault(letter) + 1;
            DrawLabel(letter == 'B' ? "B" : $"{letter}{counts[letter]}", _toView(new Vector2((float)objective.X, (float)objective.Y)));
        }

        if (runs.Count == 0)
        {
            return;
        }

        var start = runs[0].Points[0];
        DrawCircle(null, new Pen(Brushes.Lime, 3), _toView(start), EndRadius);
        var last = route.Objectives.MaxBy(objective => objective.Progress);
        DrawCircle(null, new Pen(Brushes.Red, 3), _toView(last is null ? start : new Vector2((float)last.X, (float)last.Y)), EndRadius);
    }

    private void DrawWalk(WalkedRoute walk)
    {
        foreach (var segment in walk.Segments)
        {
            var pen = segment.Kind switch
            {
                RouteSegmentKind.Walked => new Pen(new SolidColorBrush(PhaseColors[RoutePhase.Free]), 3),
                RouteSegmentKind.Controlled => new Pen(new SolidColorBrush(ControlledColor), 3),
                RouteSegmentKind.Teleport => new Pen(new SolidColorBrush(TeleportColor), 1.5, Dashes),
                _ => new Pen(new SolidColorBrush(UnreachedColor), 2, Dashes)
            };
            DrawPolyline(pen, segment.Points.Select(ToScreen).ToArray());
        }
    }

    private void DrawProgression(IReadOnlyList<ProgressionStep> steps, bool joins)
    {
        if (steps.Count == 0)
        {
            return;
        }

        if (joins)
        {
            DrawPolyline(new Pen(new SolidColorBrush(Color.FromRgb(0, 255, 255)), 2.5, Dashes), steps.Select(step => ToScreen(step.Position)).ToArray());
        }

        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            var center = ToView(steps[stepIndex].Position);
            DrawCircle(Brushes.Black, new Pen(Brushes.Cyan, 2), center, LargeDotRadius + 1);
            DrawLabel((stepIndex + 1).ToString(CultureInfo.InvariantCulture), center);
        }
    }

    private static Vector2 ToScreen(Vector3 point) => new(point.X, point.Y - point.Z);

    private Point ToView(Vector3 point) => _toView(ToScreen(point));

    private void DrawPolyline(IPen pen, IReadOnlyList<Vector2> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(_toView(points[0]), false);
            foreach (var point in points.Skip(1))
            {
                figure.LineTo(_toView(point));
            }

            figure.EndFigure(false);
        }

        _context.DrawGeometry(null, pen, geometry);
    }

    private void DrawLabel(string text, Point at)
    {
        if (!IsVisible(at, LabelSize * 2))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, LabelSize, Brushes.White);
        var origin = new Point(at.X + 6, at.Y - 6 - formatted.Height);
        _context.DrawRectangle(LabelBack, null, new Rect(origin.X - 2, origin.Y - 1, formatted.Width + 4, formatted.Height + 2));
        _context.DrawText(formatted, origin);
    }

    private void DrawCircle(IBrush? fill, IPen? pen, Point center, double radius)
    {
        if (IsVisible(center, radius))
        {
            _context.DrawEllipse(fill, pen, center, radius, radius);
        }
    }

    private void DrawLine(IPen pen, Point from, Point to)
    {
        var bounds = new Rect(from, to).Normalize();
        if (bounds.Intersects(_view.Inflate(1)))
        {
            _context.DrawLine(pen, from, to);
        }
    }

    private bool IsVisible(Point center, double radius) => new Rect(center.X - radius, center.Y - radius, 2 * radius, 2 * radius).Intersects(_view);
}

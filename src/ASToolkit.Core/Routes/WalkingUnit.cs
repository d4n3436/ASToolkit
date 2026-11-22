using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Core.Routes;

internal sealed class WalkingUnit
{
    private readonly WalkWorld _world;
    private readonly GameObject _unit;
    private readonly RouteSegmentKind _kind;
    private WalkGrid? _grid;
    private int _openCount = -1;
    private int _cell = -1;

    public WalkingUnit(WalkWorld world, GameObject unit, Vector3 position, RouteSegmentKind kind)
    {
        _world = world;
        _unit = unit;
        _kind = kind;
        Position = position;
    }

    public Vector3 Position { get; private set; }

    public bool CanWalk(Vector3 goal, float reach, int timeLine)
    {
        UpdateGrid(timeLine);
        return _cell >= 0 && _grid!.TryFindPath(_cell, goal, reach, out _);
    }

    public bool TryWalk(Vector3 goal, float reach, int timeLine, List<RouteSegment> segments, out float length, PathPreference? preference = null)
    {
        length = 0;
        UpdateGrid(timeLine);
        if (_cell < 0 || !_grid!.TryFindPath(_cell, goal, reach, out var path, preference))
        {
            return false;
        }

        length = AddPath(path, segments);
        _cell = path[^1];
        Position = _grid.GetPoint(_cell);
        return true;
    }

    public void MoveTo(Vector3 position)
    {
        Position = position;
        _cell = _grid?.GetCell(position) ?? -1;
    }

    private void UpdateGrid(int timeLine)
    {
        int openCount = _world.CountOpen(timeLine);
        if (_grid is not null && openCount == _openCount)
        {
            return;
        }

        _grid = _world.BuildGrid(_unit, timeLine, _world.Pressed);
        _openCount = openCount;
        _cell = _grid.GetCell(Position);
    }

    private float AddPath(List<int> path, List<RouteSegment> segments)
    {
        float walked = 0;
        var points = new List<Vector3> { _grid!.GetPoint(path[0]) };
        for (int i = 1; i < path.Count; i++)
        {
            var point = _grid.GetPoint(path[i]);
            if (_grid.IsLink(path[i - 1], path[i]))
            {
                segments.Add(new RouteSegment(_kind, Simplify(points)));
                segments.Add(new RouteSegment(RouteSegmentKind.Teleport, [points[^1], point]));
                points = [point];
                continue;
            }

            walked += Vector2.Distance(points[^1].AsVector2(), point.AsVector2());
            points.Add(point);
        }

        segments.Add(new RouteSegment(_kind, Simplify(points)));
        return walked;
    }

    // Keeps the points where the direction or the height changes
    private static Vector3[] Simplify(List<Vector3> points)
    {
        if (points.Count < 3)
        {
            return points.ToArray();
        }

        var kept = new List<Vector3> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (points[i] - points[i - 1] != points[i + 1] - points[i])
            {
                kept.Add(points[i]);
            }
        }

        kept.Add(points[^1]);
        return kept.ToArray();
    }
}

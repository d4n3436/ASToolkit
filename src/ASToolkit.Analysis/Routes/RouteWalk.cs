using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Analysis.Scripting;

namespace ASToolkit.Analysis.Routes;

internal sealed class RouteWalk
{
    private const float StepReach = 64;

    private readonly WalkWorld _world;
    private readonly GameObject _playerObject;
    private readonly WalkingUnit _player;
    private readonly List<(UnitControl Control, WalkingUnit Unit)> _controls = [];
    private readonly IReadOnlyList<ProgressionStep> _steps;
    private readonly HashSet<int> _done = [];
    private readonly List<OpeningTrigger> _pending;
    private readonly List<LevelSkip> _skips;
    private readonly List<QuestDetour> _detours;
    private readonly List<RouteSegment> _segments = [];
    private readonly List<ProgressionStep> _order = [];
    private WalkingUnit? _controlled;
    private float _walked;
    private int _unreached;
    private int _begun;

    public RouteWalk(WalkWorld world, MapResourceBuilder map, ObjectTable objects, LevelLogic logic, GameObject playerObject, Vector3 start)
    {
        _world = world;
        _playerObject = playerObject;
        _player = new WalkingUnit(world, playerObject, start, RouteSegmentKind.Walked);
        foreach (var control in logic.Controls)
        {
            var unit = map.Sprites.FirstOrDefault(sprite => sprite.Id == control.Unit);
            if (unit is not null && objects.TryFind(control.Unit, out var unitObject))
            {
                _controls.Add((control, new WalkingUnit(world, unitObject, unit.Position, RouteSegmentKind.Controlled)));
            }
        }

        _steps = logic.Progression;
        _pending = logic.Triggers.ToList();
        _skips = logic.Skips.ToList();
        _detours = logic.Detours.ToList();
    }

    public WalkedRoute Run()
    {
        int next = 0;
        while (next < _steps.Count)
        {
            if (_done.Contains(next))
            {
                next++;
                continue;
            }

            int i = PickNearest(next);
            _done.Add(i);
            WalkStep(_steps[i]);
        }

        return new WalkedRoute { Segments = _segments, Steps = _order, WalkedPixels = (int)MathF.Round(_walked), UnreachedSteps = _unreached };
    }

    private void WalkStep(ProgressionStep step)
    {
        TakeSkip(step);
        int first = _segments.Count;
        int timeLine = Math.Max(step.TimeLine, _begun);
        bool reached = TryReach(step, timeLine, out float length);
        while (!reached && _pending.Count > 0 && TryVisitTrigger(step, timeLine, out float triggerLength))
        {
            _walked += triggerLength;
            reached = TryReach(step, timeLine, out length);
        }

        while (!reached && _detours.Count > 0)
        {
            var detour = _detours[0];
            _detours.RemoveAt(0);
            if (detour.TimeLine > timeLine && TryWalkDetour(detour, timeLine))
            {
                timeLine = Math.Max(step.TimeLine, _begun);
                reached = TryReach(step, timeLine, out length);
            }
        }

        if (!reached)
        {
            _unreached++;
            _segments.Add(new RouteSegment(RouteSegmentKind.Unreached, [_player.Position, step.Position]));
            _player.MoveTo(step.Position);
            _order.Add(step);
            return;
        }

        _walked += length;
        var points = _segments.Skip(first).Where(segment => segment.Kind != RouteSegmentKind.Teleport).SelectMany(segment => Densify(segment.Points)).ToArray();
        FireTriggers(points, timeLine);
        _order.AddRange(MarkDestroyed(points));
        _order.Add(step);
    }

    // Moves the player at once when a key press has become available
    private void TakeSkip(ProgressionStep step)
    {
        var skip = _skips.FirstOrDefault(candidate => candidate.TimeLine <= step.TimeLine);
        if (skip is null)
        {
            return;
        }

        _skips.Remove(skip);
        _segments.Add(new RouteSegment(RouteSegmentKind.Teleport, [_player.Position, skip.Destination]));
        _player.MoveTo(skip.Destination);
        _controlled = null;
        _order.Add(new ProgressionStep(skip.Destination, 0, skip.Label, step.TimeLine));
    }

    private bool TryReach(ProgressionStep target, int timeLine, out float length)
    {
        float reach = GetReach(target);
        if (_player.TryWalk(target.Position, reach, timeLine, _segments, out length))
        {
            _controlled = null;
            return true;
        }

        if (_controlled is not null && _controlled.TryWalk(target.Position, reach, timeLine, _segments, out _))
        {
            return true;
        }

        return TryTakeControl(target, timeLine, out length);
    }

    private bool TryTakeControl(ProgressionStep step, int timeLine, out float length)
    {
        _controlled = null;
        length = 0;
        float reach = GetReach(step);
        foreach (var (control, unit) in _controls)
        {
            float controlReach = WalkWorld.GetAreaReach(control.Radius);
            if (!_player.CanWalk(control.Position, controlReach, timeLine) || !unit.CanWalk(step.Position, reach, timeLine))
            {
                continue;
            }

            _player.TryWalk(control.Position, controlReach, timeLine, _segments, out length);
            unit.TryWalk(step.Position, reach, timeLine, _segments, out _);
            _controlled = unit;
            return true;
        }

        return false;
    }

    // Goes to the trigger area from which the step can be reached once the triggers have opened what they open
    private bool TryVisitTrigger(ProgressionStep step, int timeLine, out float length)
    {
        length = 0;
        foreach (bool isStrict in new[] { true, false })
        {
            foreach (var trigger in _pending.Where(candidate => candidate.TimeLine <= timeLine).ToList())
            {
                var opened = new HashSet<int>(_world.Pressed);
                opened.UnionWith(trigger.Handles);
                opened.UnionWith(_pending.SelectMany(candidate => candidate.Handles));
                var openedGrid = _world.BuildGrid(_playerObject, timeLine, opened);
                var failed = new HashSet<int>();
                var preference = new PathPreference(step.Position, point => openedGrid.CanReach(point, step.Position, GetReach(step), failed), isStrict);
                if (!_player.TryWalk(trigger.Position, WalkWorld.GetAreaReach(trigger.Radius), timeLine, _segments, out length, preference))
                {
                    continue;
                }

                _pending.Remove(trigger);
                _controlled = null;
                _world.Pressed.UnionWith(trigger.Handles);
                if (!trigger.IsProximity)
                {
                    _order.Add(new ProgressionStep(trigger.Position, trigger.Radius, trigger.Label, step.TimeLine));
                }

                return true;
            }
        }

        return false;
    }

    // Walks the steps of a quest that opens sprites and tells whether the quest has begun
    private bool TryWalkDetour(QuestDetour detour, int timeLine)
    {
        var walkedSteps = new List<ProgressionStep>();
        foreach (var detourStep in detour.Steps)
        {
            if (!TryReach(detourStep, timeLine, out float length))
            {
                break;
            }

            _walked += length;
            walkedSteps.Add(detourStep);
        }

        _order.AddRange(walkedSteps);
        if (walkedSteps.Count < detour.Steps.Count)
        {
            return false;
        }

        _begun = Math.Max(_begun, detour.TimeLine);
        return true;
    }

    private void FireTriggers(Vector3[] points, int timeLine)
    {
        foreach (var trigger in _pending.Where(candidate => candidate.TimeLine <= timeLine
                     && points.Any(point => TriggerDistance.Contains(point, candidate.Position, candidate.Radius))).ToList())
        {
            _pending.Remove(trigger);
            _world.Pressed.UnionWith(trigger.Handles);
        }
    }

    // Marks the targets that a walk passes within a shot of, in the order it passes them
    private List<ProgressionStep> MarkDestroyed(Vector3[] points)
    {
        var passed = new List<(int PointIndex, int StepIndex)>();
        for (int i = 0; i < _steps.Count; i++)
        {
            var target = _steps[i];
            if (!target.IsTarget || _done.Contains(i))
            {
                continue;
            }

            int pointIndex = Array.FindIndex(points, point => GetDistance(point, target.Position) <= target.Radius);
            if (pointIndex >= 0)
            {
                passed.Add((pointIndex, i));
                _done.Add(i);
            }
        }

        return passed.OrderBy(pass => pass.PointIndex).Select(pass => _steps[pass.StepIndex]).ToList();
    }

    // Picks the nearest of the targets that share the label of the next step
    private int PickNearest(int next)
    {
        int best = next;
        for (int i = next; i < _steps.Count && _steps[i].IsTarget && _steps[i].Label == _steps[next].Label; i++)
        {
            if (!_done.Contains(i) && GetDistance(_steps[i].Position, _player.Position) < GetDistance(_steps[best].Position, _player.Position))
            {
                best = i;
            }
        }

        return best;
    }

    private static float GetReach(ProgressionStep step) => step is { IsTarget: false, Radius: > 0 } ? WalkWorld.GetAreaReach(step.Radius) : Math.Max(StepReach, step.Radius);

    private static float GetDistance(Vector3 first, Vector3 second) => Vector2.Distance(first.AsVector2(), second.AsVector2());

    private static IEnumerable<Vector3> Densify(IReadOnlyList<Vector3> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            yield return points[i];
            if (i + 1 >= points.Count)
            {
                continue;
            }

            int count = (int)(Vector3.Distance(points[i], points[i + 1]) / WalkGrid.CellSize);
            for (int part = 1; part < count; part++)
            {
                yield return Vector3.Lerp(points[i], points[i + 1], (float)part / count);
            }
        }
    }
}

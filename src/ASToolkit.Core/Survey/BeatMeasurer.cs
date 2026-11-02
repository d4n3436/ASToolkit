using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the beats of a surveyed level from its fights, landmarks, pickups and phases.
/// </summary>
public sealed class BeatMeasurer
{
    private const int IntensityWindow = 1600;

    private readonly LevelSurvey _survey;
    private readonly LevelProgression _progression;
    private readonly (int Start, int End)[] _spans;

    /// <summary>
    /// Initializes a new instance of the <see cref="BeatMeasurer"/> class.
    /// </summary>
    public BeatMeasurer(LevelSurvey survey, LevelProgression progression)
    {
        _survey = survey;
        _progression = progression;
        _spans = progression.Phases.Select(GetSpan).ToArray();
    }

    /// <summary>
    /// Returns the beats of the level in tour order.
    /// </summary>
    public LevelBeat[] Measure()
    {
        if (_progression.Phases.Count == 0)
        {
            return [];
        }

        var fights = _survey.Encounters
            .Where(encounter => encounter.HitPoints > 0)
            .ToArray();

        var beats = new List<LevelBeat>();
        foreach (var fight in fights)
        {
            var kind = fight.Kind switch
            {
                EncounterKind.Boss => BeatKind.Boss,
                EncounterKind.PlacedGroup => BeatKind.Fight,
                _ => BeatKind.Ambush
            };
            beats.Add(Create(kind, fight.Place, fight.Route, fight.HitPoints, 0));
        }

        foreach (var landmark in _survey.Landmarks)
        {
            BeatKind? kind = landmark.Kind switch
            {
                LandmarkKind.Breaker or LandmarkKind.Generator or LandmarkKind.DynamiteCharges => BeatKind.Key,
                LandmarkKind.Checkpoint => BeatKind.Checkpoint,
                LandmarkKind.MountedCannon or LandmarkKind.BlackoutTrigger => BeatKind.SetPiece,
                LandmarkKind.DynamiteSite or LandmarkKind.ExitTrigger or LandmarkKind.RouteEnd => BeatKind.Goal,
                _ => null
            };
            if (kind is not null)
            {
                beats.Add(Create(kind.Value, landmark.Place, landmark.Route, 0, 0));
            }
        }

        foreach (var phase in _progression.Phases.Skip(1))
        {
            var first = phase.Places
                .Select(GetPlace)
                .Where(place => place.FirstVisit is not null)
                .MinBy(place => place.FirstVisit);

            if (first is not null)
            {
                beats.Add(Create(BeatKind.Lock, first.Id, first.FirstVisit!.Value, 0, 0));
            }
        }

        foreach (var item in _survey.Items.Where(item => item.Category == ItemCategory.Weapon))
        {
            beats.Add(Create(BeatKind.Weapon, item.Place, item.Route, 0, 1));
        }

        foreach (var pile in _survey.Items.Where(item => item.Category is not (null or ItemCategory.Weapon or ItemCategory.Dynamite)).GroupBy(item => item.Place))
        {
            bool stash = pile.Key >= 1 && GetPlace(pile.Key).Kind == PlaceKind.SecretCloset;
            beats.Add(Create(stash ? BeatKind.Stash : BeatKind.Reward, pile.Key, pile.Min(item => item.Route), 0, pile.Count()));
        }

        return beats
            .Select(beat => beat with { Intensity = (float)fights.Where(fight => Math.Abs(fight.Route - beat.Route) * 2 <= IntensityWindow).Sum(fight => fight.HitPoints) / IntensityWindow })
            .OrderBy(beat => beat.Route)
            .ThenBy(beat => beat.Kind)
            .ToArray();
    }

    private (int Start, int End) GetSpan(LevelPhase phase)
    {
        var visits = _survey.Route
            .Where(visit => phase.Places.Contains(visit.Place))
            .ToArray();

        return visits.Length == 0 ? (0, 0) : (visits.Min(visit => visit.Enter), visits.Max(visit => visit.Leave));
    }

    private LevelPlace GetPlace(int id) => _survey.Places[id - 1];

    private LevelBeat Create(BeatKind kind, int place, int route, int hitPoints, int items)
    {
        int index = _progression.GetPhase(place) ?? FindPhaseAt(route);
        var phase = _progression.Phases[index];
        (int start, int end) = _spans[index];
        float position = end > start ? Math.Clamp((route - start) / (float)(end - start), 0, 1) : 0;
        int depth = phase.Path.Contains(place) ? 0 : phase.Branches.Select(branch => branch.Depths.GetValueOrDefault(place)).DefaultIfEmpty(0).Max();
        return new LevelBeat(kind, index, place, route, position, 0, hitPoints, items, depth);
    }

    private int FindPhaseAt(int route)
    {
        int found = 0;
        for (int index = 0; index < _spans.Length; index++)
        {
            if (_spans[index].Start <= route && _spans[index].End > _spans[index].Start)
            {
                found = index;
            }
        }

        return found;
    }
}

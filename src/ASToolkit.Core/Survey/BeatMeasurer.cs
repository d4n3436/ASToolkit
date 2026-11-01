using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Provides the beats of a surveyed level from its fights, landmarks, pickups and section boundaries.
/// </summary>
public sealed class BeatMeasurer
{
    private const int IntensityWindow = 1600;

    private readonly LevelSurvey _survey;
    private readonly IReadOnlyList<LevelSection> _sections;
    private readonly Dictionary<int, int> _sectionOf;

    /// <summary>
    /// Initializes a new instance of the <see cref="BeatMeasurer"/> class.
    /// </summary>
    public BeatMeasurer(LevelSurvey survey, IReadOnlyList<LevelSection> sections)
    {
        _survey = survey;
        _sections = sections;
        _sectionOf = sections.SelectMany(section => section.Places.Select(place => (Place: place, section.Index))).ToDictionary(entry => entry.Place, entry => entry.Index);
    }

    /// <summary>
    /// Returns the beats of the level in tour order.
    /// </summary>
    public LevelBeat[] Measure()
    {
        if (_sections.Count == 0)
        {
            return [];
        }

        var fights = _survey.Encounters.Where(encounter => encounter.HitPoints > 0).ToArray();
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

        foreach (var section in _sections)
        {
            var boundary = section.Boundary;
            if (boundary is not null && boundary.Value.Kind is BoundaryKind.LockedDoor or BoundaryKind.Blackout or BoundaryKind.Fence)
            {
                beats.Add(Create(BeatKind.Lock, boundary.Value.ToPlace, section.TourStart, 0, 0));
            }
        }

        foreach (var item in _survey.Items.Where(item => item.Category == ItemCategory.Weapon))
        {
            beats.Add(Create(BeatKind.Weapon, item.Place, item.Route, 0, 1));
        }

        foreach (var pile in _survey.Items.Where(item => item.Category is not (null or ItemCategory.Weapon or ItemCategory.Dynamite)).GroupBy(item => item.Place))
        {
            bool stash = pile.Key >= 1 && _survey.Places[pile.Key - 1].Kind == PlaceKind.SecretCloset;
            beats.Add(Create(stash ? BeatKind.Stash : BeatKind.Reward, pile.Key, pile.Min(item => item.Route), 0, pile.Count()));
        }

        return beats
            .Select(beat => beat with { Intensity = (float)fights.Where(fight => Math.Abs(fight.Route - beat.Route) * 2 <= IntensityWindow).Sum(fight => fight.HitPoints) / IntensityWindow })
            .OrderBy(beat => beat.Route)
            .ThenBy(beat => beat.Kind)
            .ToArray();
    }

    private LevelBeat Create(BeatKind kind, int place, int route, int hitPoints, int items)
    {
        if (!_sectionOf.TryGetValue(place, out int index))
        {
            index = _sections.LastOrDefault(section => section.TourStart <= route)?.Index ?? 0;
        }

        var section = _sections[index];
        float position = section.TourEnd > section.TourStart ? Math.Clamp((route - section.TourStart) / (float)(section.TourEnd - section.TourStart), 0, 1) : 0;
        return new LevelBeat(kind, index, place, route, position, 0, hitPoints, items, section.Depths.GetValueOrDefault(place));
    }
}

using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the fight parameters fitted to a style's original levels, such as fights per screen of floor, spawner sizes and spacing, and the size and place of a phase's largest fight.
/// </summary>
public sealed record EncounterModel
{
    private const float DefaultSpacing = 20;
    private const int DefaultSpawnerSize = 10;
    private const float DefaultGeneratorsPerSite = 1;
    private const float DefaultClimaxShare = 0.4f;
    private const float DefaultFightsPerScreen = 1;
    private const float DefaultSpawnerSpacing = 120;
    private const float DefaultSpawnerReach = 3000;
    private const float LowQuantile = 0.1f;
    private const float HighQuantile = 0.9f;
    private const float ScreenArea = 1280 * 720;
    private const float DefaultClimaxPosition = 0.3f;

    /// <summary>
    /// Gets the median spacing of the monsters of a standing group, in pixels.
    /// </summary>
    public required FittedValue<float> Spacing { get; init; }

    /// <summary>
    /// Gets the counts of monsters the spawners of the original ambushes release, in ascending order.
    /// </summary>
    public required FittedValue<IReadOnlyList<int>> SpawnerSizes { get; init; }

    /// <summary>
    /// Gets the ambushes released from spawn closets per regular room of a basement level.
    /// </summary>
    public required FittedValue<float> ClosetsPerRoom { get; init; }

    /// <summary>
    /// Gets the endless generators per dynamite site by wall set.
    /// </summary>
    public required IReadOnlyDictionary<WallSet, FittedValue<float>> GeneratorsPerSite { get; init; }

    /// <summary>
    /// Gets the fights a level holds per screen of its floor.
    /// </summary>
    public required FittedValue<float> FightsPerScreen { get; init; }

    /// <summary>
    /// Gets the least distance in pixels between two spawners of one fight.
    /// </summary>
    public required FittedValue<float> SpawnerSpacing { get; init; }

    /// <summary>
    /// Gets the farthest distance in pixels from a spawner to its trigger.
    /// </summary>
    public required FittedValue<float> SpawnerReach { get; init; }

    /// <summary>
    /// Gets the median share of a phase's hit points its largest fight holds.
    /// </summary>
    public required FittedValue<float> ClimaxShare { get; init; }

    /// <summary>
    /// Gets the positions of the original phases' largest fights along their phases, from 0 at the entrance to 1 at the end, in ascending order.
    /// </summary>
    public required FittedValue<IReadOnlyList<float>> ClimaxPositions { get; init; }

    /// <summary>
    /// Fits the model to the original levels.
    /// </summary>
    public static EncounterModel Fit(IReadOnlyList<ReferenceLevel> levels)
    {
        var encounters = levels.SelectMany(level => level.Survey.Encounters).ToArray();
        float[] spacings = encounters.Where(encounter => encounter.Kind == EncounterKind.PlacedGroup && encounter.Count > 1).Select(encounter => (float)encounter.Spacing).ToArray();
        int[] sizes = encounters.Where(encounter => encounter.Kind == EncounterKind.Ambush).Select(encounter => Math.Max(1, encounter.Count / Math.Max(1, encounter.Spawners))).Order().ToArray();
        var basement = levels.Where(level => level.Survey.WallSet == WallSet.Basement).ToArray();
        int closets = basement.Sum(level => level.Survey.Encounters.Count(encounter => encounter.Kind == EncounterKind.Ambush && encounter.Source == SpawnSource.SpawnCloset));
        var beats = levels.Select(level => new BeatMeasurer(level.Survey, new ProgressionMeasurer(level.Survey).Measure())).ToArray();
        var climaxes = beats.SelectMany(GetClimaxes).ToArray();
        int fights = beats.Sum(measurer => measurer.GroupFights().Count);
        float screens = levels.Sum(level => level.Survey.Places.Sum(place => place.AreaPixels)) / ScreenArea;
        var spawners = levels.SelectMany(GetSpawners).ToArray();
        float[] gaps = spawners.Where(spawner => spawner.Spacing is not null).Select(spawner => spawner.Spacing!.Value).ToArray();
        float[] reaches = spawners.Select(spawner => spawner.Reach).ToArray();
        var generators = Rate(levels.Sum(CountGenerators), levels.Sum(CountSites), DefaultGeneratorsPerSite);
        return new EncounterModel
        {
            Spacing = Median(spacings, DefaultSpacing),
            SpawnerSizes = sizes.Length < LinearFit.MinSamples ? new FittedValue<IReadOnlyList<int>>([DefaultSpawnerSize], sizes.Length, IsBackedOff: true) : new FittedValue<IReadOnlyList<int>>(sizes, sizes.Length, IsBackedOff: false),
            ClosetsPerRoom = Rate(closets, basement.Sum(level => level.Atlas.CountRegularRooms()), 0),
            GeneratorsPerSite = levels
                .GroupBy(level => level.Survey.WallSet)
                .ToDictionary(group => group.Key, group => Rate(group.Sum(CountGenerators), group.Sum(CountSites), generators.Value)),
            FightsPerScreen = levels.Count < LinearFit.MinSamples ? new FittedValue<float>(DefaultFightsPerScreen, levels.Count, IsBackedOff: true) : new FittedValue<float>(fights / screens, levels.Count, IsBackedOff: false),
            SpawnerSpacing = Quantile(gaps, LowQuantile, DefaultSpawnerSpacing),
            SpawnerReach = Quantile(reaches, HighQuantile, DefaultSpawnerReach),
            ClimaxShare = Median(climaxes.Select(climax => climax.Share).ToArray(), DefaultClimaxShare),
            ClimaxPositions = climaxes.Length < LinearFit.MinSamples ? new FittedValue<IReadOnlyList<float>>([DefaultClimaxPosition], climaxes.Length, IsBackedOff: true)
                : new FittedValue<IReadOnlyList<float>>(climaxes.Select(climax => climax.Position).Order().ToArray(), climaxes.Length, IsBackedOff: false)
        };
    }

    private static IEnumerable<(float Share, float Position)> GetClimaxes(BeatMeasurer measurer) =>
        measurer.Measure()
            .Where(beat => beat.Kind is BeatKind.Fight or BeatKind.Ambush or BeatKind.Boss && beat.HitPoints > 0)
            .GroupBy(beat => beat.Phase)
            .Where(phase => phase.Count() >= 2)
            .Select(phase => (phase.Max(beat => beat.HitPoints) / (float)phase.Sum(beat => beat.HitPoints), phase.MaxBy(beat => beat.HitPoints).Position));

    private static IEnumerable<(float? Spacing, float Reach)> GetSpawners(ReferenceLevel level)
    {
        var spawners = level.Map.Sprites
            .Where(sprite => sprite.Id == KnownObject.Spawner)
            .Select(sprite => (Point: new PointF(sprite.Position.X, sprite.Position.Y), SpawnerProgram.Parse(sprite.Payload)?.Trigger))
            .Where(spawner => spawner.Trigger is not null)
            .ToArray();

        foreach (var spawner in spawners)
        {
            var mates = spawners.Where(other => other.Trigger == spawner.Trigger && other.Point != spawner.Point).ToArray();
            yield return (mates.Length == 0 ? null : mates.Min(other => GetDistance(other.Point, spawner.Point)), GetDistance(spawner.Point, new PointF(spawner.Trigger!.Value.X, spawner.Trigger.Value.Y)));
        }
    }

    private static float GetDistance(PointF first, PointF second) => MathF.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    private static FittedValue<float> Quantile(float[] samples, float quantile, float parent)
    {
        if (samples.Length < LinearFit.MinSamples)
        {
            return new FittedValue<float>(parent, samples.Length, IsBackedOff: true);
        }

        Array.Sort(samples);
        return new FittedValue<float>(samples[(int)(quantile * (samples.Length - 1))], samples.Length, IsBackedOff: false);
    }

    private static int CountGenerators(ReferenceLevel level) =>
        level.Survey.Encounters.Where(encounter => encounter.Kind == EncounterKind.EndlessGenerators).Sum(encounter => encounter.Count);

    private static int CountSites(ReferenceLevel level) => level.Survey.Landmarks.Count(landmark => landmark.Kind == LandmarkKind.DynamiteSite);

    private static FittedValue<float> Median(float[] samples, float parent)
    {
        if (samples.Length < LinearFit.MinSamples)
        {
            return new FittedValue<float>(parent, samples.Length, IsBackedOff: true);
        }

        Array.Sort(samples);
        return new FittedValue<float>(samples[samples.Length / 2], samples.Length, IsBackedOff: false);
    }

    private static FittedValue<float> Rate(int count, int per, float parent) =>
        per < LinearFit.MinSamples ? new FittedValue<float>(parent, per, IsBackedOff: true) : new FittedValue<float>(count / (float)per, per, IsBackedOff: false);
}

using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the pickup and objective parameters fitted to a style's original levels, such as crate shares, pile sizes and spots,
/// closet stashes, and where sites and charges lie along the walk.
/// </summary>
public sealed record EconomyModel
{
    private const float PileReach = 45;
    private const int DefaultPileSize = 1;
    private const int DefaultClosetSize = 3;
    private const float DefaultOffTourWeight = 1;
    private const float DefaultSitePosition = 0.6f;
    private const float DefaultChargeLead = 0.1f;
    private static readonly LandmarkKind[] Objectives = [LandmarkKind.Breaker, LandmarkKind.BlackoutTrigger, LandmarkKind.Generator, LandmarkKind.DynamiteSite, LandmarkKind.ExitTrigger, LandmarkKind.RouteEnd];

    /// <summary>
    /// Gets the share of each category's pickups that lie in crates.
    /// </summary>
    public required IReadOnlyDictionary<ItemCategory, FittedValue<float>> CrateShares { get; init; }

    /// <summary>
    /// Gets the sizes of the original piles of loose pickups.
    /// </summary>
    public required FittedValue<IReadOnlyList<int>> PileSizes { get; init; }

    /// <summary>
    /// Gets the walls around each category's original piles.
    /// </summary>
    public required IReadOnlyDictionary<ItemCategory, FittedValue<IReadOnlyList<PartSpot>>> PileSpots { get; init; }

    /// <summary>
    /// Gets the pickups per pixel of floor in rooms off the walks from the start to the objectives against rooms on them.
    /// </summary>
    public required FittedValue<float> OffTourWeight { get; init; }

    /// <summary>
    /// Gets the categories of the pickups other than weapons in the original secret closets.
    /// </summary>
    public required FittedValue<IReadOnlyList<ItemCategory>> ClosetItems { get; init; }

    /// <summary>
    /// Gets the pickup counts of the original secret closets that hold any.
    /// </summary>
    public required FittedValue<IReadOnlyList<int>> ClosetSizes { get; init; }

    /// <summary>
    /// Gets the share of spawn closets that hold pickups.
    /// </summary>
    public required FittedValue<float> StockedSpawnClosets { get; init; }

    /// <summary>
    /// Gets the walks from the start to the original dynamite sites as shares of the level's longest walk.
    /// </summary>
    public required FittedValue<IReadOnlyList<float>> SitePositions { get; init; }

    /// <summary>
    /// Gets the median walk from a batch of charges to the next site of its phase as a share of the level's longest walk.
    /// </summary>
    public required FittedValue<float> ChargeLead { get; init; }

    /// <summary>
    /// Gets the share of levels with dynamite sites that hold charges in the start room.
    /// </summary>
    public required FittedValue<float> StartBatchShare { get; init; }

    /// <summary>
    /// Gets the share of levels with a checkpoint.
    /// </summary>
    public required FittedValue<float> CheckpointShare { get; init; }

    /// <summary>
    /// Fits the model to a style's original levels, with the charge values of too few samples taken from the pooled levels.
    /// </summary>
    public static EconomyModel Fit(IReadOnlyList<ReferenceLevel> levels, IReadOnlyList<ReferenceLevel> pooled)
    {
        var loose = levels
            .SelectMany(level => level.Survey.Items.Where(item => item.Category is not null and not ItemCategory.Dynamite).Select(item => (Level: level, Item: item)))
            .ToArray();
        
        var closets = levels
            .SelectMany(level => level.Survey.Places.Where(place => place.Kind == PlaceKind.SecretCloset).Select(place => level.Survey.Items.Where(item => item.Place == place.Id && item.Category is not null).ToArray()))
            .ToArray();
        
        bool[] spawnClosets = levels
            .SelectMany(level => level.Survey.Places.Where(place => place.Kind == PlaceKind.SpawnCloset).Select(place => level.Survey.Items.Any(item => item.Place == place.Id && item.Category is not null)))
            .ToArray();
        
        var piles = levels.SelectMany(GetPiles).ToArray();
        var stocked = closets.Where(closet => closet.Length > 0).ToArray();
        float[] positions = levels.SelectMany(GetSites).Order().ToArray();
        
        return new EconomyModel
        {
            CrateShares = Enum.GetValues<ItemCategory>().ToDictionary(category => category, category =>
            {
                var items = loose.Where(entry => entry.Item.Category == category).ToArray();
                return items.Length < LinearFit.MinSamples ? new FittedValue<float>(0, items.Length, IsBackedOff: true)
                    : new FittedValue<float>(items.Count(entry => entry.Item.InCrate) / (float)items.Length, items.Length, IsBackedOff: false);
            }),
            PileSizes = Sample(piles.Select(pile => pile.Size).Order().ToArray(), DefaultPileSize),
            PileSpots = Enum.GetValues<ItemCategory>().ToDictionary(category => category, category => Sample(piles.Where(pile => pile.Category == category).Select(pile => pile.Spot).ToArray(), PartSpot.Center)),
            OffTourWeight = FitOffTourWeight(levels),
            ClosetItems = Sample(stocked.SelectMany(closet => closet).Select(item => item.Category!.Value).Where(category => category != ItemCategory.Weapon).Order().ToArray(), ItemCategory.Money),
            ClosetSizes = Sample(stocked.Select(closet => closet.Length).Order().ToArray(), DefaultClosetSize),
            StockedSpawnClosets = spawnClosets.Length < LinearFit.MinSamples ? new FittedValue<float>(0, spawnClosets.Length, IsBackedOff: true)
                : new FittedValue<float>(spawnClosets.Count(stockedCloset => stockedCloset) / (float)spawnClosets.Length, spawnClosets.Length, IsBackedOff: false),
            SitePositions = Sample(positions, DefaultSitePosition),
            ChargeLead = FitChargeLead(levels, FitChargeLead(pooled, DefaultChargeLead).Value),
            StartBatchShare = FitStartBatches(levels, FitStartBatches(pooled, 0).Value),
            CheckpointShare = levels.Count < LinearFit.MinSamples ? new FittedValue<float>(0, levels.Count, IsBackedOff: true)
                : new FittedValue<float>(levels.Count(level => level.Analysis.Checkpoints.Count > 0) / (float)levels.Count, levels.Count, IsBackedOff: false)
        };
    }

    private static FittedValue<IReadOnlyList<T>> Sample<T>(T[] samples, T parent) => samples.Length < LinearFit.MinSamples
        ? new FittedValue<IReadOnlyList<T>>([parent], samples.Length, IsBackedOff: true)
        : new FittedValue<IReadOnlyList<T>>(samples, samples.Length, IsBackedOff: false);

    private static IEnumerable<(ItemCategory Category, int Size, PartSpot Spot)> GetPiles(ReferenceLevel level)
    {
        var model = level.Model;
        var closets = level.Survey.Places.Where(place => place.Kind is PlaceKind.SecretCloset or PlaceKind.SpawnCloset).Select(place => place.Id).ToHashSet();
        foreach (var group in level.Survey.Items.Where(item => item.Category is not null and not ItemCategory.Dynamite && !item.InCrate && !closets.Contains(item.Place)).GroupBy(item => item.Category!.Value))
        {
            foreach (var pile in group.Select(item => new PointF(item.Position.X, item.Position.Y)).ToArray().Cluster(PileReach))
            {
                var cell = model.Lattice.GetCell(pile[0].X, pile[0].Y);
                var sides = model.Edges.GetWalledSides(cell, model.Cells.Contains, static kind => kind.IsClosed);
                yield return (group.Key, pile.Count, sides.IsCorner ? PartSpot.Corner : sides != WallSides.None ? PartSpot.Wall : PartSpot.Center);
            }
        }
    }

    private static FittedValue<float> FitOffTourWeight(IReadOnlyList<ReferenceLevel> levels)
    {
        float onItems = 0, onArea = 0, offItems = 0, offArea = 0;
        int samples = 0;
        foreach (var level in levels)
        {
            var walked = GetWalkedPlaces(level.Survey);
            foreach (var place in level.Survey.Places.Where(place => place.Kind is not (PlaceKind.SecretCloset or PlaceKind.SpawnCloset)))
            {
                int items = level.Survey.Items.Count(item => item.Place == place.Id && item.Category is not null and not ItemCategory.Dynamite);
                if (walked.Contains(place.Id))
                {
                    (onItems, onArea) = (onItems + items, onArea + place.AreaPixels);
                }
                else
                {
                    (offItems, offArea, samples) = (offItems + items, offArea + place.AreaPixels, samples + 1);
                }
            }
        }

        return samples < LinearFit.MinSamples || onItems == 0 || offArea == 0 ? new FittedValue<float>(DefaultOffTourWeight, samples, IsBackedOff: true)
            : new FittedValue<float>(offItems / offArea / (onItems / onArea), samples, IsBackedOff: false);
    }

    private static HashSet<int> GetWalkedPlaces(LevelSurvey survey)
    {
        int start = survey.Landmarks.First(landmark => landmark.Kind == LandmarkKind.PlayerStart).Place;
        var parents = new Dictionary<int, int> { [start] = start };
        var queue = new Queue<int>([start]);
        while (queue.TryDequeue(out int place))
        {
            foreach (var connection in survey.Connections.Where(connection => (connection.A == place || connection.B == place) && connection.Kind is not (ConnectionKind.JammedDoor or ConnectionKind.SpawnWall or ConnectionKind.MonsterHole)))
            {
                int next = connection.A == place ? connection.B : connection.A;
                if (parents.TryAdd(next, place))
                {
                    queue.Enqueue(next);
                }
            }
        }

        var walked = new HashSet<int>();
        foreach (int target in survey.Landmarks.Where(landmark => Objectives.Contains(landmark.Kind) && parents.ContainsKey(landmark.Place)).Select(landmark => landmark.Place))
        {
            int place = target;
            while (walked.Add(place))
            {
                place = parents[place];
            }
        }

        return walked;
    }

    private static float[] GetSites(ReferenceLevel level) => level.Analysis.Elements
        .Where(element => element.Kind == ElementKind.DynamiteSite)
        .Select(element => element.Distance / GetLongestWalk(level))
        .Order()
        .ToArray();

    private static float GetLongestWalk(ReferenceLevel level) => Math.Max(1, level.Analysis.Elements.Max(element => element.Distance));

    private static FittedValue<float> FitChargeLead(IReadOnlyList<ReferenceLevel> levels, float parent)
    {
        var leads = new List<float>();
        foreach (var level in levels)
        {
            var progression = new ProgressionMeasurer(level.Survey).Measure();
            int GetPhase(int place) => progression.Phases.Count == 0 ? 0 : progression.GetPhase(place) ?? -1;
            var sites = level.Analysis.Elements
                .Where(element => element.Kind == ElementKind.DynamiteSite)
                .Select(element => (Walk: element.Distance / GetLongestWalk(level), Phase: GetPhase(level.Survey.FindPlace(element.X, element.Y))))
                .ToArray();

            foreach (var batch in level.Analysis.Elements.Where(element => element.Id == KnownObject.Dynamite).GroupBy(element => level.Survey.FindPlace(element.X, element.Y)))
            {
                float walk = batch.Min(element => element.Distance) / GetLongestWalk(level);
                float next = sites.Where(site => site.Phase == GetPhase(batch.Key) && site.Walk > walk).Select(site => site.Walk).DefaultIfEmpty(-1).Min();
                if (next >= 0)
                {
                    leads.Add(next - walk);
                }
            }
        }

        leads.Sort();
        return leads.Count < LinearFit.MinSamples ? new FittedValue<float>(parent, leads.Count, IsBackedOff: true) : new FittedValue<float>(leads[leads.Count / 2], leads.Count, IsBackedOff: false);
    }

    private static FittedValue<float> FitStartBatches(IReadOnlyList<ReferenceLevel> levels, float parent)
    {
        var withSites = levels.Where(level => level.Analysis.Elements.Any(element => element.Kind == ElementKind.DynamiteSite)).ToArray();
        int started = withSites.Count(level =>
        {
            int start = level.Survey.Landmarks.First(landmark => landmark.Kind == LandmarkKind.PlayerStart).Place;
            return level.Analysis.Elements.Any(element => element.Id == KnownObject.Dynamite && level.Survey.FindPlace(element.X, element.Y) == start);
        });

        return withSites.Length < LinearFit.MinSamples ? new FittedValue<float>(parent, withSites.Length, IsBackedOff: true)
            : new FittedValue<float>(started / (float)withSites.Length, withSites.Length, IsBackedOff: false);
    }
}

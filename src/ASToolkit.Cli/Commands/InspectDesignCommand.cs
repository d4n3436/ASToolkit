using System.Globalization;
using System.Text;
using ASToolkit.Cli.Analysis;
using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Reference;
using SixLabors.ImageSharp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectDesignCommand : Command<InspectDesignSettings>
{
    private const int MaxLevelsAtOnce = 3;
    private const float RareFrameShare = 0.05f;
    private const int MinFrameSamples = 5;
    private const float MinWallShare = 0.3f;

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private static readonly KnownObject[] TallFurniture = [KnownObject.Cabinet, KnownObject.Table, KnownObject.Shelves, KnownObject.BigComputer];
    private static readonly KnownObject[] FacedFurniture = [KnownObject.Cabinet, KnownObject.Table, KnownObject.Container, KnownObject.Shelves];

    public override int Execute(CommandContext context, InspectDesignSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.Out);
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        var campaign = ReferenceLevelSet.CampaignLevels.Select(level => (Name: ReferenceLevelSet.GetCampaignName(level), Path: Path.Combine(settings.Game, "Maps", ReferenceLevelSet.GetCampaignName(level)))).ToList();
        var addOns = AddOnLevels.Select(name => (Name: name, Path: Path.Combine(settings.Game, "Maps", name))).ToList();
        var generated = Generate(settings, cancellationToken);
        var renderer = settings.Images ? new MapRenderer(settings.Game, objects) : null;

        var all = campaign.Concat(addOns).Concat(generated).ToArray();
        var loader = new LevelLoader(objects);
        var measures = new DesignMeasures?[all.Length];
        var levels = new ReferenceLevel?[all.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        AnsiConsole.Status().Start("Measuring the levels", _ => Parallel.For(0, all.Length, options, index =>
        {
            var level = loader.Load(all[index].Name, all[index].Path);
            if (level is null)
            {
                return;
            }

            levels[index] = level;
            measures[index] = DesignMeasurer.Measure(level.Model, level.Analysis, level.Survey, objects);
            if (renderer is not null)
            {
                using var rendered = renderer.Render(level.Map.Sprites, settings.Scale, level.Map.Graphics);
                PlaceOverlay.Draw(rendered, level.Survey, level.Analysis);
                rendered.Image.SaveAsPng(Path.Combine(settings.Out, GetFileName(all[index].Name) + "_places.png"));
            }
        }));

        WriteLevels(settings, all.Select(entry => entry.Name).ToArray(), measures);
        var campaignLevels = levels.Take(campaign.Count).OfType<ReferenceLevel>().ToArray();
        var addOnLevels = levels.Skip(campaign.Count).Take(addOns.Count).OfType<ReferenceLevel>().ToArray();
        var generatedLevels = levels.Skip(campaign.Count + addOns.Count).OfType<ReferenceLevel>().ToArray();
        var campaignRelations = ObjectRelationTable.Build(campaignLevels, objects);
        WriteRelations(settings, campaignRelations, ObjectRelationTable.Build(addOnLevels, objects), ObjectRelationTable.Build(generatedLevels, objects),
            [campaignRelations.CountCrowded(campaignLevels, objects), campaignRelations.CountCrowded(addOnLevels, objects), campaignRelations.CountCrowded(generatedLevels, objects)]);
        var originals = measures.Take(campaign.Count).OfType<DesignMeasures>().ToArray();
        var addOnMeasures = measures.Skip(campaign.Count).Take(addOns.Count).OfType<DesignMeasures>().ToArray();
        var candidates = measures.Skip(campaign.Count + addOns.Count).OfType<DesignMeasures>().ToArray();
        var rows = BuildGapRows(originals, addOnMeasures, candidates);
        WriteGaps(settings, rows, candidates.Length);

        var table = new Table().AddColumns("Measure", "Campaign", "Add-ons", $"Generated ({settings.Seeds} seeds)");
        foreach ((string measure, string original, string addOn, string candidate) in rows)
        {
            table.AddRow(Markup.Escape(measure), Markup.Escape(original), Markup.Escape(addOn), Markup.Escape(candidate));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Wrote design.tsv, design.md and relations.md to [green]{Markup.Escape(settings.Out)}[/].");
        return measures.All(measure => measure is not null) ? 0 : 1;
    }

    private static List<(string Name, string Path)> Generate(InspectDesignSettings settings, CancellationToken cancellationToken)
    {
        var levels = new List<(string Name, string Path)>();
        if (settings.Seeds == 0)
        {
            return levels;
        }

        var (generator, _, _) = GeneratorFactory.Create(settings.Game);
        var jobs = Enumerable.Range(1, settings.Seeds).SelectMany(seed => ReferenceLevelSet.CampaignLevels.Select(level => (Seed: seed, Level: level))).ToArray();
        foreach ((int seed, int level) in jobs)
        {
            string folder = Path.Combine(settings.Out, $"seed_{seed}");
            Directory.CreateDirectory(folder);
            levels.Add(($"seed_{seed}/{ReferenceLevelSet.GetCampaignName(level)}", Path.Combine(folder, ReferenceLevelSet.GetCampaignName(level))));
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        AnsiConsole.Status().Start("Generating the levels", _ => Parallel.For(0, jobs.Length, options, index =>
        {
            if (!File.Exists(levels[index].Path))
            {
                generator.Generate(jobs[index].Seed, jobs[index].Level).Write(levels[index].Path, generator.GetTemplatePath(jobs[index].Level));
            }
        }));

        return levels;
    }

    private static void WriteLevels(InspectDesignSettings settings, string[] names, DesignMeasures?[] measures)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Join('\t', "map", "places", "loops", "revisit", "start area", "first fight", "hit points by fifth", "chairs at tables", "corpses on blood",
            "office decor in passages", "health against walls", "pickup piles", "health after fights", "room proportion", "fight gap", "repeated rooms", "cannon walk",
            "gated share", "key to gate", "key walk", "first generator", "blocked share", "spread"));
        for (int index = 0; index < names.Length; index++)
        {
            var measure = measures[index];
            if (measure is null)
            {
                text.AppendLine($"{names[index]}\tno route");
                continue;
            }

            var signature = measure.Signature;
            text.AppendLine(string.Join('\t', names[index], signature.Places, signature.Loops, Format(signature.RevisitShare), signature.StartPixels, Format(signature.FirstFight),
                string.Join(' ', signature.HitPointsByFifth.Select(Format)), Format(measure.ChairsAtTables), Format(measure.CorpsesOnBlood), Format(measure.OfficeDecorInPassages),
                Format(measure.HealthAgainstWalls), Format(measure.PickupPiles), Format(measure.HealthAfterFights), Format(measure.RoomProportion), Format(measure.FightGap),
                measure.RepeatedRooms, Format(measure.CannonWalk), Format(signature.GatedShare), Format(signature.KeyToGate), Format(signature.KeyWalk),
                Format(signature.FirstGenerator), Format(signature.BlockedShare), Format(measure.Spread)));
        }

        File.WriteAllText(Path.Combine(settings.Out, "design.tsv"), text.ToString());
    }

    private static List<(string Measure, string Original, string AddOn, string Candidate)> BuildGapRows(DesignMeasures[] originals, DesignMeasures[] addOns, DesignMeasures[] candidates)
    {
        var rows = new List<(string, string, string, string)>();
        AddShare("chairs within 60 px of a table", measure => measure.ChairsAtTables);
        AddShare("tables with a chair within 60 px", measure => measure.TablesWithChairs);
        AddShare("test tubes within 60 px of a table", measure => measure.TubesOnTables);
        AddShare("lab equipment within 60 px of a table", measure => measure.LabEquipmentOnTables);
        AddShare("scattered paper within 60 px of a table", measure => measure.PaperOnTables);
        AddShare("computers away from walls", measure => measure.ComputersAwayFromWalls);
        AddShare("lab big computers with a chair within 200 px", measure => measure.BigComputersWithChairs);
        AddShare("corpses on blood (40 px)", measure => measure.CorpsesOnBlood);
        AddShare("office and lab decor in passages", measure => measure.OfficeDecorInPassages);
        AddShare("office and lab decor in storage rooms", measure => measure.OfficeDecorInStorage);
        foreach (var id in TallFurniture)
        {
            AddShare($"{id} against far walls only", measure => CountWalls(measure, id, sample => sample.FarOnly));
            AddShare($"{id} against near walls only", measure => CountWalls(measure, id, sample => sample.NearOnly));
        }

        var rare = FindRareFrames(originals);
        foreach (var id in FacedFurniture)
        {
            AddShare($"{id} in a frame used under 5% against its wall", measure => CountRareFrames(measure, id, rare));
        }

        AddShare("loose money against a wall", measure => measure.MoneyAgainstWalls);
        AddShare("loose money in a corner", measure => measure.MoneyInCorners);
        AddShare("loose health against a wall", measure => measure.HealthAgainstWalls);
        AddShare("loose ammo against a wall", measure => measure.AmmoAgainstWalls);
        AddShare("loose armor against a wall", measure => measure.ArmorAgainstWalls);
        AddShare("pickup piles of two or more (45 px)", measure => measure.PickupPiles);
        AddShare("health placed after a fight", measure => measure.HealthAfterFights);
        AddShare("ammo placed before a fight", measure => measure.AmmoBeforeFights);
        AddShare("loose money over 200 px of walk off the route", measure => measure.MoneyOffRoute);
        AddShare("health in crates", measure => measure.HealthInCrates);
        AddShare("ammo in crates", measure => measure.AmmoInCrates);
        AddShare("armor in crates", measure => measure.ArmorInCrates);
        AddMedian("loose pickups per pile (45 px), median of levels", measure => measure.PileSize);
        AddShare("solid floor pieces within 30 px of a doorway", measure => measure.SolidNearDoorways);
        AddShare("hittable floor pieces within 30 px of a doorway", measure => measure.HittableNearDoorways);
        AddShare("monitors within 40 px of a doorway", measure => measure.MonitorsNearDoorways);
        AddShare("solid or hittable floor pieces standing inside another", measure => measure.PiecesInsideOthers);
        AddShare("loose pickups under a solid or hittable piece", measure => measure.PickupsUnderPieces);
        AddShare("monitors within 25 px of a cabinet or shelves", measure => measure.MonitorsOnTallFurniture);
        AddShare("monitors standing on a piece", measure => measure.MonitorsOnSupports);
        AddShare("boxes on shelves, a cabinet or a table", measure => measure.BoxesOnFurniture);
        AddShare("test tubes, lab equipment and computers standing on a piece", measure => measure.DeskTopsOnSupports);
        AddShare("shelves with another shelf within 70 px", measure => measure.ShelvesGrouped);
        rows.Add(("longest cabinet run", Format(MaxOf(originals)), Format(MaxOf(addOns)), Format(MaxOf(candidates))));
        AddShare("missiles in triplets", measure => measure.MissilesInTriplets);
        AddShare("pad groups mirrored about their middle", measure => measure.MirroredPads);
        AddList("wall inscriptions within 230 px of the cannon", measure => measure.CannonStripes);
        AddList("open floor around the cannon (px)", measure => measure.CannonOpenPixels);
        AddShare("hit points within 1,200 px of the cannon", measure => measure.CannonHitPoints);
        AddList("walk from the start to the cannon (px)", measure => measure.CannonWalk);
        rows.Add(("room length over width, median of levels", Format(Median(originals, measure => measure.RoomProportion)), Format(Median(addOns, measure => measure.RoomProportion)), Format(Median(candidates, measure => measure.RoomProportion))));
        rows.Add(("levels with repeated room layouts", CountRepeated(originals), CountRepeated(addOns), CountRepeated(candidates)));
        rows.Add(("tour gap between fights with 5% of the hit points, median of levels", Format(Median(originals, measure => measure.FightGap)), Format(Median(addOns, measure => measure.FightGap)),
            Format(Median(candidates, measure => measure.FightGap))));
        AddShare("dynamite sites on screen 0.2 of the tour before they are reached", measure => measure.SitesPassedEarly);
        AddMedian("lab door-graph cycles, median of levels", measure => measure.Graph?.Cycles);
        AddMedian("lab main body share of the floor, median of levels", measure => measure.Graph?.MainBodyShare);
        AddMedian("lab largest branch share of the floor, median of levels", measure => measure.Graph?.LargestBranchShare);
        AddMedian("lab hub rooms from the start, median of levels", measure => measure.Graph?.HubHops);
        AddMedian("lab deepest objective in rooms off the main body, median of levels", measure => measure.Graph?.ObjectiveDepths.DefaultIfEmpty(0).Max());
        AddMedian("lab square rooms, median of levels", measure => measure.Graph?.SquareShare);
        AddMedian("lab small rooms with no notch, median of levels", measure => measure.Graph?.SmallRectangleShare);
        AddMedian("lab mid rooms with no notch, median of levels", measure => measure.Graph?.MidRectangleShare);
        AddMedian("lab large rooms with no notch, median of levels", measure => measure.Graph?.LargeRectangleShare);
        AddMedian("lab notches open outside, median of levels", measure => measure.Graph?.OutsideNotchShare);
        AddMedian("lab ambush hit points into rooms the tour has left, median of levels", measure => measure.Graph?.AbandonedAmbushShare);
        AddMedian("lab first-section rooms re-entered after the gate, median of levels", measure => measure.Graph?.SectionReentries);
        AddMedian("lab bare rooms, median of levels", measure => measure.Graph?.BareRooms);
        AddMedian("floor off the tour, median of levels", measure => measure.Signature.OffRouteShare);
        AddMedian("tour share re-walked, median of levels", measure => measure.Signature.RevisitShare);
        AddMedian("first fight, share of the tour, median of levels", measure => measure.Signature.FirstFight);
        AddMedian("fifth of the tour with the most hit points, median of levels", measure => FindPeakFifth(measure.Signature.HitPointsByFifth));
        AddMedian("spawned monsters per 1,000 spawned hit points, median of levels", measure => measure.SpawnedPerThousand);
        AddMedian("largest spawner (monsters), median of levels", measure => measure.LargestSpawner);
        return rows;

        void AddMedian(string name, Func<DesignMeasures, float?> get) =>
            rows.Add((name, Format(Median(originals, get)), Format(Median(addOns, get)), Format(Median(candidates, get))));

        void AddShare(string name, Func<DesignMeasures, MeasureShare> get) =>
            rows.Add((name, Format(Sum(originals, get)), Format(Sum(addOns, get)), Format(Sum(candidates, get))));

        void AddList(string name, Func<DesignMeasures, int?> get) =>
            rows.Add((name, string.Join(", ", originals.Select(get).OfType<int>().Order().Select(Format)), string.Join(", ", addOns.Select(get).OfType<int>().Order().Select(Format)),
                FormatRange(candidates.Select(get).OfType<int>().ToArray())));

        static MeasureShare Sum(DesignMeasures[] measures, Func<DesignMeasures, MeasureShare> get) => measures.Aggregate(default(MeasureShare), (sum, measure) => sum + get(measure));

        static float? FindPeakFifth(IReadOnlyList<int> fifths) => fifths.Count == 0 || fifths.Sum() == 0 ? null : fifths.Select((hitPoints, index) => (hitPoints, index)).MaxBy(fifth => fifth.hitPoints).index + 1;

        static int MaxOf(DesignMeasures[] measures) => measures.Select(measure => measure.LongestCabinetRun).DefaultIfEmpty(0).Max();

        static string CountRepeated(DesignMeasures[] measures) => $"{measures.Count(measure => measure.RepeatedRooms > 0)} of {measures.Length}";
    }

    private static MeasureShare CountWalls(DesignMeasures measure, KnownObject id, Func<WallSample, bool> predicate)
    {
        var samples = measure.Walls.Where(sample => sample.Id == id).ToArray();
        return new MeasureShare(samples.Count(predicate), samples.Length);
    }

    private static Dictionary<(KnownObject Id, WallSides Side), HashSet<byte>> FindRareFrames(DesignMeasures[] originals)
    {
        var rare = new Dictionary<(KnownObject, WallSides), HashSet<byte>>();
        var groups = originals.SelectMany(measure => measure.Walls).Where(sample => sample.Single != WallSides.None).GroupBy(sample => (sample.Id, sample.Single));
        foreach (var group in groups)
        {
            int total = group.Count();
            if (total < MinFrameSamples)
            {
                continue;
            }

            var frames = group.GroupBy(sample => sample.Dir).ToDictionary(frame => frame.Key, frame => frame.Count());
            rare[group.Key] = Enumerable.Range(0, 256).Select(dir => (byte)dir).Where(dir => frames.GetValueOrDefault(dir) < RareFrameShare * total).ToHashSet();
        }

        return rare;
    }

    private static MeasureShare CountRareFrames(DesignMeasures measure, KnownObject id, Dictionary<(KnownObject Id, WallSides Side), HashSet<byte>> rare)
    {
        var samples = measure.Walls.Where(sample => sample.Id == id && rare.ContainsKey((id, sample.Single))).ToArray();
        return new MeasureShare(samples.Count(sample => rare[(id, sample.Single)].Contains(sample.Dir)), samples.Length);
    }

    // Compare each relation the campaign shows with the same relation in the add-ons and the generated levels, then the door clearances
    private static void WriteRelations(InspectDesignSettings settings, ObjectRelationTable campaign, ObjectRelationTable addOns, ObjectRelationTable generated,
        (int Crowded, int Count)[] crowded)
    {
        var text = new StringBuilder();
        text.AppendLine("| theme | object | stands by | campaign | lift | add-ons | generated |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var theme in new[] { LevelTheme.Lab, LevelTheme.Military })
        {
            foreach (var relations in campaign.GetRelations(theme))
            {
                foreach (var anchor in relations.Anchors)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"| {theme} | {relations.Id} | {anchor.Id} | {anchor.Share:P0} of {relations.Count} | {anchor.Lift:0} | {FormatShare(addOns, theme, relations.Id, anchor.Id)} | {FormatShare(generated, theme, relations.Id, anchor.Id)} |");
                }

                if (relations.WallShare >= 2 * relations.RandomWallShare && relations.WallShare >= MinWallShare)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"| {theme} | {relations.Id} | a wall | {relations.WallShare:P0} of {relations.Count} | {relations.WallShare / Math.Max(relations.RandomWallShare, 0.01f):0} | {FormatWallShare(addOns, theme, relations.Id)} | {FormatWallShare(generated, theme, relations.Id)} |");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("| theme | floor pieces | closest to a doorway, all but 5% | add-ons | generated |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var theme in new[] { LevelTheme.Lab, LevelTheme.Military })
        {
            foreach (var blocking in new[] { BlockingClass.Solid, BlockingClass.Hittable })
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {theme} | {blocking} | {FormatDistance(campaign.GetDoorClearance(theme, blocking))} | {FormatDistance(addOns.GetDoorClearance(theme, blocking))} | {FormatDistance(generated.GetDoorClearance(theme, blocking))} |");
            }
        }

        text.AppendLine();
        text.AppendLine("| measure | campaign | add-ons | generated |");
        text.AppendLine("|---|---|---|---|");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| pieces closer to another than the campaign stands the pair | {string.Join(" | ", crowded.Select(entry => $"{entry.Crowded:N0} of {entry.Count:N0} ({entry.Crowded / (float)Math.Max(1, entry.Count):P0})"))} |");
        text.AppendLine();
        text.AppendLine("| theme | anchor | companion | per anchor | add-ons | generated |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var theme in new[] { LevelTheme.Lab, LevelTheme.Military })
        {
            foreach (var anchor in Enum.GetValues<KnownObject>().Where(id => id.Family is not null || id == KnownObject.Spawner))
            {
                foreach (var companion in campaign.GetCompanions(theme, anchor))
                {
                    text.AppendLine(CultureInfo.InvariantCulture,
                        $"| {theme} | {anchor} | {companion.Id} | {companion.Rate:0.00} | {addOns.GetCompanionRate(theme, anchor, companion.Id):0.00} | {generated.GetCompanionRate(theme, anchor, companion.Id):0.00} |");
                }
            }
        }

        File.WriteAllText(Path.Combine(settings.Out, "relations.md"), text.ToString());
    }

    private static string FormatShare(ObjectRelationTable table, LevelTheme theme, KnownObject id, KnownObject anchor) =>
        table.GetRelations(theme, id) is null ? "-" : table.GetAnchorShare(theme, id, anchor).ToString("P0", CultureInfo.InvariantCulture);

    private static string FormatWallShare(ObjectRelationTable table, LevelTheme theme, KnownObject id)
    {
        var relations = table.GetRelations(theme, id);
        return relations is null ? "-" : relations.WallShare.ToString("P0", CultureInfo.InvariantCulture);
    }

    private static string FormatDistance(float? distance) => distance is null ? "-" : $"{distance.Value:0} px";

    private static void WriteGaps(InspectDesignSettings settings, List<(string Measure, string Original, string AddOn, string Candidate)> rows, int candidates)
    {
        var text = new StringBuilder();
        text.AppendLine($"| measure | campaign | add-ons | generated ({candidates} levels) |");
        text.AppendLine("|---|---|---|---|");
        foreach ((string measure, string original, string addOn, string candidate) in rows)
        {
            text.AppendLine($"| {measure} | {original} | {addOn} | {candidate} |");
        }

        File.WriteAllText(Path.Combine(settings.Out, "design.md"), text.ToString());
    }

    private static float? Median(DesignMeasures[] measures, Func<DesignMeasures, float?> get)
    {
        float[] sorted = measures.Select(get).OfType<float>().Order().ToArray();
        return sorted.Length == 0 ? null : sorted[sorted.Length / 2];
    }

    private static string GetFileName(string name) => Path.ChangeExtension(name, null).Replace('/', '_').Replace('\\', '_');

    private static string Format(MeasureShare share) => share.Total == 0 ? "-" : $"{Format(share.Count)} of {Format(share.Total)} ({Math.Round(100 * share.Value!.Value)}%)";

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Format(int? value) => value is null ? "-" : Format(value.Value);

    private static string Format(float? value) => value is null ? "-" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatRange(int[] values) => values.Length == 0 ? "-" : values.Min() == values.Max() ? Format(values[0]) : $"{Format(values.Min())} to {Format(values.Max())}";
}

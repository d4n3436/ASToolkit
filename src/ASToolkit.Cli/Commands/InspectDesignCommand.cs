using System.Diagnostics;
using System.Globalization;
using System.Text;
using ASToolkit.Cli.Analysis;
using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Campaign;
using ASToolkit.Core.Generation.Output;
using ASToolkit.Core.Generation;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Rendering;
using ASToolkit.Core.Survey;
using SixLabors.ImageSharp;
using Spectre.Console.Cli;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal sealed class InspectDesignCommand : Command<InspectDesignSettings>
{
    private static readonly int MaxLevelsAtOnce = Environment.ProcessorCount;
    private const float RareFrameShare = 0.05f;
    private const int MinFrameSamples = 5;
    private const float MinWallShare = 0.3f;

    private static readonly string[] AddOnLevels =
    [
        "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    private static readonly (string Title, string Folder, string Money, int Levels)[] Campaigns =
    [
        ("Campaign", "", "LevelMoney", 10), ("Add-on 1", "ADDON/", "LevelMoneyAddon", 5), ("Add-on 2", "ADDON2/", "LevelMoneyAddon2", 5)
    ];

    private const int PlannedCampaigns = 100;

    private static readonly string[] Prices = ["WeaponPrice", "AmmoPrice", "ItemPrice", "GettingAmmo"];

    private static readonly KnownObject[] TallFurniture = [KnownObject.Cabinet, KnownObject.Table, KnownObject.Shelves, KnownObject.BigComputer];
    private static readonly KnownObject[] FacedFurniture = [KnownObject.Cabinet, KnownObject.Table, KnownObject.Container, KnownObject.Shelves];

    public override int Execute(CommandContext context, InspectDesignSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.Out);
        var objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        var campaign = ReferenceLevelSet.CampaignLevels.Select(level => (Name: ReferenceLevelSet.GetCampaignName(level), Path: Path.Combine(settings.Game, "Maps", ReferenceLevelSet.GetCampaignName(level)))).ToList();
        var addOns = AddOnLevels.Select(name => (Name: name, Path: Path.Combine(settings.Game, "Maps", name))).ToList();
        var references = settings.Seeds > 0 || settings.LeaveOneOut
            ? AnsiConsole.Status().Start("Analyzing the original levels", _ => ReferenceLevelSet.Load(settings.Game, objects))
            : null;
        var fitter = new StyleFitter(objects, GameTable.Load(Path.Combine(settings.Game, "Maps", "common_table.lgc")));
        var (generated, provenance, layoutCounts) = references is null
            ? ([], [], new Dictionary<string, int>())
            : AnsiConsole.Status().Start("Generating the levels", _ => Generate(new LevelGenerator(settings.Game, objects, references, fitter.Fit(references, GenerationStyle.Campaign)), settings.Out, settings.Seeds, cancellationToken));
        var renderer = settings.Images ? new MapRenderer(settings.Game, objects) : null;

        var all = campaign.Concat(addOns).Concat(generated).ToArray();
        var loader = new LevelLoader(objects);
        var (levels, measures) = AnsiConsole.Status().Start("Measuring the levels", _ => Measure(all, loader, references, objects, level =>
        {
            if (renderer is not null)
            {
                using var rendered = renderer.Render(level.Map.Sprites, settings.Scale, level.Map.Graphics);
                PlaceOverlay.Draw(rendered, level.Survey, level.Analysis);
                rendered.Image.SaveAsPng(Path.Combine(settings.Out, GetFileName(level.Name) + "_places.png"));
            }
        }, cancellationToken));

        WriteLevels(settings, all.Select(entry => entry.Name).ToArray(), measures);
        if (generated.Count > 0)
        {
            WriteProvenance(settings, generated.Select(entry => entry.Name).ToArray(), provenance);
            WriteLayoutCounts(settings, layoutCounts);
        }

        var styles = references is null
            ? []
            : new[] { GenerationStyle.Campaign, GenerationStyle.AddOn }.Select(style => (Style: style, Model: fitter.Fit(references, style).Campaign)).ToArray();

        WriteCampaigns(settings, loader, all.Select(entry => entry.Name).Zip(levels).Where(pair => pair.Second is not null).ToDictionary(pair => pair.First, pair => pair.Second!.Survey), styles);
        var campaignLevels = levels.Take(campaign.Count).OfType<ReferenceLevel>().ToArray();
        var addOnLevels = levels.Skip(campaign.Count).Take(addOns.Count).OfType<ReferenceLevel>().ToArray();
        var generatedLevels = levels.Skip(campaign.Count + addOns.Count).OfType<ReferenceLevel>().ToArray();
        var campaignRelations = ObjectRelationTable.Build(campaignLevels, objects);
        WriteRelations(settings, campaignRelations, ObjectRelationTable.Build(addOnLevels, objects), ObjectRelationTable.Build(generatedLevels, objects),
            [campaignRelations.CountCrowded(campaignLevels, objects), campaignRelations.CountCrowded(addOnLevels, objects), campaignRelations.CountCrowded(generatedLevels, objects)]);
        var originals = measures.Take(campaign.Count).OfType<DesignMeasures>().ToArray();
        var addOnMeasures = measures.Skip(campaign.Count).Take(addOns.Count).OfType<DesignMeasures>().ToArray();
        var candidates = measures.Skip(campaign.Count + addOns.Count).OfType<DesignMeasures>().ToArray();
        var bands = new List<(string Name, Func<DesignMeasures[], float?> Value)>();
        var rows = BuildGapRows(originals, addOnMeasures, candidates, bands);
        AddNoveltyRows(rows, all.Select(entry => entry.Name).ToArray(), levels, campaign.Count, addOns.Count);
        WriteGaps(settings, rows, candidates.Length);

        var table = new Table().AddColumns("Measure", "Campaign", "Add-ons", $"Generated ({settings.Seeds} seeds)");
        foreach ((string measure, string original, string addOn, string candidate) in rows)
        {
            table.AddRow(Markup.Escape(measure), Markup.Escape(original), Markup.Escape(addOn), Markup.Escape(candidate));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"Wrote design.tsv, design.md, relations.md, campaign.md, provenance.md and layout.md to [green]{Markup.Escape(settings.Out)}[/].");
        if (settings.LeaveOneOut && references is not null)
        {
            var full = generated
                .Select(entry => entry.Name)
                .Zip(measures.Skip(campaign.Count + addOns.Count))
                .Where(pair => pair.Second is not null)
                .ToDictionary(pair => pair.First, pair => pair.Second!);

            LeaveOneOut(settings, objects, references, fitter, loader, bands, originals, full, cancellationToken);
        }

        return measures.All(measure => measure is not null) ? 0 : 1;
    }

    private static (ReferenceLevel?[] Levels, DesignMeasures?[] Measures) Measure((string Name, string Path)[] entries, LevelLoader loader, ReferenceLevelSet? references, ObjectTable objects,
        Action<ReferenceLevel> onLoaded,
        CancellationToken cancellationToken)
    {
        var levels = new ReferenceLevel?[entries.Length];
        var measures = new DesignMeasures?[entries.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        Parallel.For(0, entries.Length, options, index =>
        {
            var level = references is not null && references.LevelNames.Contains(entries[index].Name) ? references.GetLevel(entries[index].Name) : loader.Load(entries[index].Name, entries[index].Path);
            if (level is not null)
            {
                levels[index] = level;
                measures[index] = DesignMeasurer.Measure(level.Model, level.Analysis, level.Survey, objects);
                onLoaded(level);
            }
        });

        return (levels, measures);
    }

    private static void LeaveOneOut(InspectDesignSettings settings, ObjectTable objects, ReferenceLevelSet references, StyleFitter fitter, LevelLoader loader,
        List<(string Name, Func<DesignMeasures[], float?> Value)> rows, DesignMeasures[] originals, Dictionary<string, DesignMeasures> full, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.AppendLine("| left out | levels that failed | rows that left the originals' range over the same levels (full fit, left out, range) |");
        text.AppendLine("|---|---|---|");

        var bands = rows
            .Select(row => originals.Select(original => row.Value([original])).OfType<float>().ToArray())
            .Select(values => values.Length == 0 ? ((float Min, float Max)?)null : (values.Min(), values.Max()))
            .ToArray();

        var excludedLevels = StyleFitter.GetFittingLevels(GenerationStyle.Campaign);
        var generators = new LevelGenerator[excludedLevels.Count];
        var jobs = Enumerable.Range(0, excludedLevels.Count)
            .SelectMany(excluded => Enumerable.Range(1, settings.Seeds).SelectMany(seed => ReferenceLevelSet.CampaignLevels.Select(level => (Excluded: excluded, Seed: seed, Level: level))))
            .ToArray();

        var measures = new DesignMeasures?[jobs.Length];
        string?[] failures = new string?[jobs.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        AnsiConsole.Status().Start("Leaving each original out", _ =>
        {
            Parallel.For(0, excludedLevels.Count, options, index =>
                generators[index] = new LevelGenerator(settings.Game, objects, references, fitter.Fit(references, GenerationStyle.Campaign, [excludedLevels[index]])));

            Parallel.For(0, jobs.Length, options, index =>
            {
                (int excluded, int seed, int number) = jobs[index];
                string folder = Path.Combine(settings.Out, "leave-one-out", Path.GetFileNameWithoutExtension(excludedLevels[excluded]), $"seed_{seed}");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, ReferenceLevelSet.GetCampaignName(number));
                try
                {
                    var level = generators[excluded].Generate(seed, number);
                    level.Write(path, generators[excluded].GetTemplatePath(level));
                }
                catch (Exception exception)
                {
                    failures[index] = GetFailure(exception);
                    return;
                }

                var loaded = loader.Load($"seed_{seed}/{ReferenceLevelSet.GetCampaignName(number)}", path, isOriginal: false);
                measures[index] = loaded is null ? null : DesignMeasurer.Measure(loaded.Model, loaded.Analysis, loaded.Survey, objects);
            });
        });

        string[] lines = new string[excludedLevels.Count];
        for (int excludedIndex = 0; excludedIndex < excludedLevels.Count; excludedIndex++)
        {
            int[] mine = Enumerable.Range(0, jobs.Length).Where(index => jobs[index].Excluded == excludedIndex).ToArray();
            var measured = mine
                .Select(index => (Measure: measures[index], Name: $"seed_{jobs[index].Seed}/{ReferenceLevelSet.GetCampaignName(jobs[index].Level)}"))
                .Where(pair => pair.Measure is not null && full.ContainsKey(pair.Name))
                .ToArray();

            var left = new List<string>();
            for (int index = 0; index < rows.Count; index++)
            {
                var band = bands[index];
                float? fitted = rows[index].Value(measured.Select(pair => full[pair.Name]).ToArray());
                float? value = rows[index].Value(measured.Select(pair => pair.Measure!).ToArray());
                if (band is not null && fitted is not null && value is not null && IsWithin(fitted.Value, band.Value) && !IsWithin(value.Value, band.Value))
                {
                    left.Add($"{rows[index].Name} ({Format(fitted)}, {Format(value)}, {Format(band.Value.Min)} to {Format(band.Value.Max)})");
                }
            }

            string[] failed = mine
                .Where(index => failures[index] is not null)
                .GroupBy(index => (Level: ReferenceLevelSet.GetCampaignName(jobs[index].Level), Reason: failures[index]))
                .Select(group => $"{group.Key.Level} ×{group.Count()} ({group.Key.Reason})")
                .ToArray();

            lines[excludedIndex] = $"| {excludedLevels[excludedIndex]} | {(failed.Length == 0 ? "none" : string.Join(", ", failed))} | {(left.Count == 0 ? "none" : string.Join("; ", left))} |";
        }

        foreach (string line in lines)
        {
            text.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(settings.Out, "leave-one-out.md"), text.ToString());
        AnsiConsole.MarkupLine($"Wrote leave-one-out.md to [green]{Markup.Escape(settings.Out)}[/].");

        static bool IsWithin(float value, (float Min, float Max) band) => value >= band.Min && value <= band.Max;
    }

    private static (List<(string Name, string Path)> Levels, IReadOnlyDictionary<SpriteSource, int>[] Provenance, Dictionary<string, int> LayoutCounts) Generate(
        LevelGenerator generator,
        string folder, int seeds, CancellationToken cancellationToken)
    {
        var levels = new List<(string Name, string Path)>();

        var jobs = Enumerable.Range(1, seeds)
            .SelectMany(seed => ReferenceLevelSet.CampaignLevels.Select(level => (Seed: seed, Level: level)))
            .ToArray();

        foreach ((int seed, int level) in jobs)
        {
            string seedFolder = Path.Combine(folder, $"seed_{seed}");
            Directory.CreateDirectory(seedFolder);
            levels.Add(($"seed_{seed}/{ReferenceLevelSet.GetCampaignName(level)}", Path.Combine(seedFolder, ReferenceLevelSet.GetCampaignName(level))));
        }

        var provenance = new IReadOnlyDictionary<SpriteSource, int>[jobs.Length];
        var counts = new IReadOnlyDictionary<string, int>[jobs.Length];
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxLevelsAtOnce, CancellationToken = cancellationToken };
        Parallel.For(0, jobs.Length, options, index =>
        {
            File.Delete(levels[index].Path);
            var level = generator.Generate(jobs[index].Seed, jobs[index].Level);
            level.Write(levels[index].Path, generator.GetTemplatePath(level));
            provenance[index] = level.Provenance;
            counts[index] = level.LayoutCounts;
        });

        var total = new Dictionary<string, int> { ["levels"] = counts.Length };
        foreach ((string name, int count) in counts.SelectMany(count => count))
        {
            total[name] = total.GetValueOrDefault(name) + count;
        }

        return (levels, provenance, total);
    }

    private static string GetFailure(Exception exception)
    {
        var site = new StackTrace(exception).GetFrames()
            .Select(frame => frame.GetMethod())
            .FirstOrDefault(method => method?.DeclaringType?.Namespace?.StartsWith("ASToolkit", StringComparison.Ordinal) == true);

        return $"{exception.GetType().Name} in {site?.DeclaringType?.Name}.{site?.Name}";
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

    private static List<(string Measure, string Original, string AddOn, string Candidate)> BuildGapRows(DesignMeasures[] originals, DesignMeasures[] addOns, DesignMeasures[] candidates,
        List<(string Name, Func<DesignMeasures[], float?> Value)> numeric)
    {
        var rows = new List<(string, string, string, string)>();
        AddShare("chairs within 60 px of a table", measure => measure.ChairsAtTables);
        AddShare("tables with a chair within 60 px", measure => measure.TablesWithChairs);
        AddShare("test tubes within 60 px of a table", measure => measure.TubesOnTables);
        AddShare("lab equipment within 60 px of a table", measure => measure.LabEquipmentOnTables);
        AddShare("scattered paper within 60 px of a table", measure => measure.PaperOnTables);
        AddShare("computers away from walls", measure => measure.ComputersAwayFromWalls);
        AddShare("basement big computers with a chair within 200 px", measure => measure.BigComputersWithChairs);
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
        AddShare("gates whose lock the tour reaches before the key", measure => measure.LocksSeenBeforeKeys);
        AddShare("locked-door and fence gates with the key in a phase before their lock", measure => measure.KeysBeforeLocks);
        AddMedian("largest fight's position along its phase, median of levels", measure => measure.ClimaxPosition);
        AddShare("phase climaxes with fewer hit points in the next tenth of the tour", measure => measure.ReliefAfterClimaxes);
        AddMedian("basement door-graph cycles, median of levels", measure => measure.Graph?.Cycles);
        AddMedian("basement floor share of the bounding box, median of levels", measure => measure.Architecture?.Compactness);
        AddMedian("basement wall lines of 3+ cells per 1,000 cells, median of levels", measure => measure.Architecture?.WallLinesPerThousandCells);
        AddMedian("basement corridor share of the floor, median of levels", measure => measure.Architecture?.CorridorShare);
        AddMedian("basement median hall area (px²), median of levels", measure => measure.Architecture?.HallArea);
        AddMedian("basement median room area (px²), median of levels", measure => measure.Architecture?.RoomArea);
        AddMedian("basement median closet and nook area (px²), median of levels", measure => measure.Architecture?.ClosetArea);
        AddMedian("basement outline notches per side, median of levels", measure => measure.Architecture?.NotchesPerSide);
        AddMedian("basement main body share of the floor, median of levels", measure => measure.Graph?.MainBodyShare);
        AddMedian("basement largest branch share of the floor, median of levels", measure => measure.Graph?.LargestBranchShare);
        AddMedian("basement hub rooms from the start, median of levels", measure => measure.Graph?.HubHops);
        AddMedian("basement deepest objective in rooms off the main body, median of levels", measure => measure.Graph?.ObjectiveDepths.DefaultIfEmpty(0).Max());
        AddMedian("basement square rooms, median of levels", measure => measure.Graph?.SquareShare);
        AddMedian("basement small rooms with no notch, median of levels", measure => measure.Graph?.SmallRectangleShare);
        AddMedian("basement mid rooms with no notch, median of levels", measure => measure.Graph?.MidRectangleShare);
        AddMedian("basement large rooms with no notch, median of levels", measure => measure.Graph?.LargeRectangleShare);
        AddMedian("basement notches open outside, median of levels", measure => measure.Graph?.OutsideNotchShare);
        AddMedian("basement ambush hit points into rooms the tour has left, median of levels", measure => measure.Graph?.AbandonedAmbushShare);
        AddMedian("basement first-section rooms re-entered after the gate, median of levels", measure => measure.Graph?.SectionReentries);
        AddMedian("basement bare rooms, median of levels", measure => measure.Graph?.BareRooms);
        AddMedian("floor off the tour, median of levels", measure => measure.Signature.OffRouteShare);
        AddMedian("tour share re-walked, median of levels", measure => measure.Signature.RevisitShare);
        AddMedian("first fight, share of the tour, median of levels", measure => measure.Signature.FirstFight);
        AddMedian("fifth of the tour with the most hit points, median of levels", measure => FindPeakFifth(measure.Signature.HitPointsByFifth));
        AddMedian("spawned monsters per 1,000 spawned hit points, median of levels", measure => measure.SpawnedPerThousand);
        AddMedian("largest spawner (monsters), median of levels", measure => measure.LargestSpawner);
        return rows;

        void AddMedian(string name, Func<DesignMeasures, float?> get)
        {
            rows.Add((name, Format(Median(originals, get)), Format(Median(addOns, get)), Format(Median(candidates, get))));
            numeric.Add((name, measures => Median(measures, get)));
        }

        void AddShare(string name, Func<DesignMeasures, MeasureShare> get)
        {
            rows.Add((name, Format(Sum(originals, get)), Format(Sum(addOns, get)), Format(Sum(candidates, get))));
            numeric.Add((name, measures => Sum(measures, get).Value));
        }

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
        text.AppendLine("| wall set | object | stands by | campaign | lift | add-ons | generated |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var wallSet in new[] { WallSet.Basement, WallSet.Concrete })
        {
            foreach (var relations in campaign.GetRelations(wallSet))
            {
                foreach (var anchor in relations.Anchors)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"| {wallSet} | {relations.Id} | {anchor.Id} | {anchor.Share:P0} of {relations.Count} | {anchor.Lift:0} | {FormatShare(addOns, wallSet, relations.Id, anchor.Id)} | {FormatShare(generated, wallSet, relations.Id, anchor.Id)} |");
                }

                if (relations.WallShare >= 2 * relations.RandomWallShare && relations.WallShare >= MinWallShare)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"| {wallSet} | {relations.Id} | a wall | {relations.WallShare:P0} of {relations.Count} | {relations.WallShare / Math.Max(relations.RandomWallShare, 0.01f):0} | {FormatWallShare(addOns, wallSet, relations.Id)} | {FormatWallShare(generated, wallSet, relations.Id)} |");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("| wall set | floor pieces | closest to a doorway, all but 5% | add-ons | generated |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var wallSet in new[] { WallSet.Basement, WallSet.Concrete })
        {
            foreach (var blocking in new[] { BlockingClass.Solid, BlockingClass.Hittable })
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {wallSet} | {blocking} | {FormatDistance(campaign.GetDoorClearance(wallSet, blocking))} | {FormatDistance(addOns.GetDoorClearance(wallSet, blocking))} | {FormatDistance(generated.GetDoorClearance(wallSet, blocking))} |");
            }
        }

        text.AppendLine();
        text.AppendLine("| measure | campaign | add-ons | generated |");
        text.AppendLine("|---|---|---|---|");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| pieces closer to another than the campaign stands the pair | {string.Join(" | ", crowded.Select(entry => $"{entry.Crowded:N0} of {entry.Count:N0} ({entry.Crowded / (float)Math.Max(1, entry.Count):P0})"))} |");
        text.AppendLine();
        text.AppendLine("| wall set | anchor | companion | per anchor | add-ons | generated |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var wallSet in new[] { WallSet.Basement, WallSet.Concrete })
        {
            foreach (var anchor in Enum.GetValues<KnownObject>().Where(id => id.Family is not null || id == KnownObject.Spawner))
            {
                foreach (var companion in campaign.GetCompanions(wallSet, anchor))
                {
                    text.AppendLine(CultureInfo.InvariantCulture,
                        $"| {wallSet} | {anchor} | {companion.Id} | {companion.Rate:0.00} | {addOns.GetCompanionRate(wallSet, anchor, companion.Id):0.00} | {generated.GetCompanionRate(wallSet, anchor, companion.Id):0.00} |");
                }
            }
        }

        File.WriteAllText(Path.Combine(settings.Out, "relations.md"), text.ToString());
    }

    private static string FormatShare(ObjectRelationTable table, WallSet wallSet, KnownObject id, KnownObject anchor) =>
        table.GetRelations(wallSet, id) is null ? "-" : table.GetAnchorShare(wallSet, id, anchor).ToString("P0", CultureInfo.InvariantCulture);

    private static string FormatWallShare(ObjectRelationTable table, WallSet wallSet, KnownObject id)
    {
        var relations = table.GetRelations(wallSet, id);
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

    private static void AddNoveltyRows(List<(string Measure, string Original, string AddOn, string Candidate)> rows, string[] names, ReferenceLevel?[] levels, int campaign, int addOns)
    {
        var originals = Enumerable.Range(0, campaign + addOns)
            .Where(index => levels[index] is not null)
            .Select(index => (names[index], levels[index]!.Model, levels[index]!.Survey))
            .ToArray();

        var novelty = new NoveltyMeasurer(originals);
        var copied = new MeasureShare[names.Length];
        string?[] equal = new string?[names.Length];
        Parallel.For(0, names.Length, index =>
        {
            var level = levels[index];
            if (level is not null)
            {
                copied[index] = novelty.CountCopiedPieces(names[index], level.Model);
                equal[index] = novelty.FindEqualDoorGraph(names[index], level.Survey);
            }
        });

        rows.Add(("decor past blood and shadows, and monsters, in groups of 3+ with 2+ ids matching another original within 2 px", Format(Sum(copied.Take(campaign))),
            Format(Sum(copied.Skip(campaign).Take(addOns))), Format(Sum(copied.Skip(campaign + addOns)))));
        rows.Add(("levels whose door graph has the shape of another original's", CountEqual(equal.Take(campaign)), CountEqual(equal.Skip(campaign).Take(addOns)),
            CountEqual(equal.Skip(campaign + addOns))));

        static MeasureShare Sum(IEnumerable<MeasureShare> shares) => shares.Aggregate(default(MeasureShare), (total, share) => total + share);

        static string CountEqual(IEnumerable<string?> found)
        {
            string[] matches = found.OfType<string>().ToArray();
            return matches.Length == 0 ? "0" : $"{matches.Length} ({string.Join(", ", matches.Distinct())})";
        }
    }

    private static void WriteLayoutCounts(InspectDesignSettings settings, Dictionary<string, int> counts)
    {
        var text = new StringBuilder();
        text.AppendLine("| building or doors count | total | per level |");
        text.AppendLine("|---|---|---|");
        int levels = Math.Max(1, counts.GetValueOrDefault("levels"));
        foreach ((string name, int count) in counts.Where(pair => pair.Key != "levels").OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"| {name} | {count} | {Format(count / (float)levels)} |");
        }

        File.WriteAllText(Path.Combine(settings.Out, "layout.md"), text.ToString());
    }

    private static void WriteProvenance(InspectDesignSettings settings, string[] names, IReadOnlyDictionary<SpriteSource, int>[] provenance)
    {
        var sources = Enum.GetValues<SpriteSource>();
        var text = new StringBuilder();
        text.AppendLine($"| level | {string.Join(" | ", sources)} | copied share of the pieces past the layout |");
        text.AppendLine($"|---|{string.Concat(sources.Select(_ => "---|"))}---|");

        var rows = names
            .Zip(provenance)
            .GroupBy(pair => Path.GetFileName(pair.First))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (Name: group.Key, Counts: sources.Select(source => group.Sum(pair => pair.Second.GetValueOrDefault(source))).ToArray()))
            .ToList();

        rows.Add(("all", sources.Select((_, index) => rows.Sum(row => row.Counts[index])).ToArray()));
        foreach ((string name, int[] counts) in rows)
        {
            int copied = sources
                .Select((source, index) => source is SpriteSource.CopiedWindow or SpriteSource.CopiedPart or SpriteSource.CopiedScene or SpriteSource.CopiedFormation ? counts[index] : 0)
                .Sum();

            text.AppendLine($"| {name} | {string.Join(" | ", counts.Select(Format))} | {Format(copied / (float)Math.Max(1, counts.Sum() - counts[(int)SpriteSource.Layout]))} |");
        }

        File.WriteAllText(Path.Combine(settings.Out, "provenance.md"), text.ToString());
    }

    private static void WriteCampaigns(InspectDesignSettings settings, LevelLoader loader, Dictionary<string, LevelSurvey> surveys, IReadOnlyList<(GenerationStyle Style, CampaignModel Model)> styles)
    {
        var table = GameTable.Load(Path.Combine(settings.Game, "Maps", "common_table.lgc"));
        var text = new StringBuilder();
        foreach ((string title, string folder, string money, int count) in Campaigns)
        {
            var levels = new List<LevelSurvey>();
            for (int number = 1; number <= count; number++)
            {
                string name = folder + ReferenceLevelSet.GetCampaignName(number);
                if (!surveys.TryGetValue(name, out var survey))
                {
                    var loaded = loader.Load(name, Path.Combine(settings.Game, "Maps", name));
                    if (loaded is null)
                    {
                        break;
                    }

                    survey = loaded.Survey;
                }

                levels.Add(survey);
            }

            text.AppendLine($"## {title}");
            text.AppendLine();
            text.AppendLine("| level | floor set | gate | hit points | spawned share | monster kinds | new monster kinds | floor (px²) | tour (px) | phases | health | armor | ammo | cash | new weapons | new weapons before their ammo | end money |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var level in new CampaignMeasurer(table.GetArray(money)).Measure(levels))
            {
                text.AppendLine($"| {level.Level} | {level.FloorSet} | {level.GateStyle} | {Format(level.HitPoints)} | {Format(level.SpawnedShare)} | {level.MonsterKinds.Count} | "
                    + $"{string.Join(", ", level.NewMonsterKinds)} | {Format(level.FloorPixels)} | {Format(level.TourPixels)} | {level.Phases} | {Format(level.Pickups.GetValueOrDefault(ItemCategory.Health))} | "
                    + $"{Format(level.Pickups.GetValueOrDefault(ItemCategory.Armor))} | {Format(level.Pickups.GetValueOrDefault(ItemCategory.Ammo))} | {Format(level.Money)} | "
                    + $"{string.Join(", ", level.NewWeapons)} | {level.NewWeaponsBeforeAmmo} | {Format(level.EndMoney)} |");
            }

            text.AppendLine();
        }

        foreach (var (style, model) in styles)
        {
            WriteCampaignModel(text, style, model);
        }

        text.AppendLine("## Prices");
        text.AppendLine();
        foreach (string name in Prices)
        {
            text.AppendLine($"- {name}: {string.Join(", ", table.GetArray(name).Select(Format))}");
        }

        File.WriteAllText(Path.Combine(settings.Out, "campaign.md"), text.ToString());
    }

    private static void WriteCampaignModel(StringBuilder text, GenerationStyle style, CampaignModel model)
    {
        text.AppendLine($"## {style} model");
        text.AppendLine();
        text.AppendLine("| parameter | value | samples | backed off |");
        text.AppendLine("|---|---|---|---|");
        var lines = new (string Name, FittedValue<LinearFit> Fit)[]
        {
            ("ln hit points", model.HitPoints), ("log-odds of the spawned share", model.SpawnedShare), ("ln end money, by ln level", model.EndMoney),
            ("weapon first level by price rank", model.WeaponLevels), ("repeated weapons", model.RepeatedWeapons), ("secret closets per Mpx² of basement walls", model.SecretClosets)
        }.Concat(model.PickupRates.Select(pair => ($"{pair.Key} per Mpx²", pair.Value)))
            .Concat(model.PickupShares.Select(pair => ($"{pair.Key} share of its category", pair.Value)));

        foreach ((string name, var fit) in lines)
        {
            text.AppendLine($"| {name} | {Format(fit.Value.Intercept)} + {Format(fit.Value.Slope)} × level ± {Format(fit.Value.Spread)} | {fit.Samples} | {(fit.IsBackedOff ? "flat" : "")} |");
        }

        foreach ((string name, var fit) in new[] { ("ln floor (px²)", model.FloorPixels), ("ln rooms per Mpx²", model.Rooms) })
        {
            text.AppendLine($"| {name} | {Format((float)fit.Value.Intercept)} + {Format((float)fit.Value.Slope)} × level + {Format((float)fit.Value.ConcreteTerm)} on concrete ± {Format((float)fit.Value.Spread)} | {fit.Samples} | {(fit.IsBackedOff ? "flat" : "")} |");
        }

        foreach (var building in model.Buildings.Value)
        {
            text.AppendLine($"| {building.Building} | share {Format(building.Share)}, position {Format(building.Position)}{(building.IsRequired ? ", required" : "")} | {building.Samples} | |");
        }

        text.AppendLine($"| affordability | {Format(model.Affordability.Value)} | {model.Affordability.Samples} | |");
        text.AppendLine($"| ammo by age (next, newest, older) | {string.Join(", ", model.AmmoAges.Value.Select(share => Format(share)))} | {model.AmmoAges.Samples} | |");
        foreach (var (wallSet, gates) in model.Gates.OrderBy(pair => pair.Key))
        {
            text.AppendLine($"| {wallSet} gates | {string.Join(", ", gates.Value.Select(gate => $"{gate.GateStyle} {Format(gate.Share)}"))} | {gates.Samples} | |");
        }

        text.AppendLine();
        var planner = new CampaignPlanner(model);
        var plans = Enumerable.Range(1, PlannedCampaigns)
            .Select(seed => planner.Plan(new Random(seed)).Levels)
            .ToArray();

        text.AppendLine($"Medians of {PlannedCampaigns} planned campaigns, with the fitted line of the hit points:");
        text.AppendLine();
        text.AppendLine("| level | commonest building | basement share | commonest gate | hit points | line | spawned share | floor (px²) | secret closets | health | armor | ammo | cash | weapons | end money | commonest donor |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int index = 1; index < model.LevelCount; index++)
        {
            var briefs = plans.Select(plan => plan[index]).ToArray();
            text.AppendLine($"| {index + 1} | {briefs.CountBy(brief => brief.Building).MaxBy(pair => pair.Value).Key} | {Format(briefs.Count(brief => brief.WallSet == WallSet.Basement) / (float)briefs.Length)} | {briefs.CountBy(brief => brief.GateStyle).MaxBy(pair => pair.Value).Key} | "
                            + $"{Format(Median(briefs.Select(brief => brief.HitPoints)))} | {Format((int)MathF.Exp(model.HitPoints.Value.Evaluate(index + 1)))} | {Format(Median(briefs.Select(brief => brief.SpawnedShare)))} | "
                            + $"{Format(Median(briefs.Select(brief => brief.FloorPixels)))} | {Format(Median(briefs.Select(brief => brief.SecretClosets)))} | {Format(Median(briefs.Select(brief => CountOf(brief, ItemCategory.Health))))} | "
                            + $"{Format(Median(briefs.Select(brief => CountOf(brief, ItemCategory.Armor))))} | {Format(Median(briefs.Select(brief => CountOf(brief, ItemCategory.Ammo))))} | "
                            + $"{Format(Median(briefs.Select(brief => CountOf(brief, ItemCategory.Money) * model.CashValue)))} | {Format(Median(briefs.Select(brief => brief.Weapons.Count)))} | "
                            + $"{Format(Median(briefs.Select(brief => brief.EndMoney)))} | "
                            + $"{briefs.CountBy(brief => brief.DonorLevel).MaxBy(pair => pair.Value).Key} |");
        }

        text.AppendLine();
        text.AppendLine("| weapon | price | first level, median of the campaigns that bring it | campaigns that bring it |");
        text.AppendLine("|---|---|---|---|");
        foreach (var weapon in model.Weapons)
        {
            int[] firsts = plans
                .Select(plan => plan.FirstOrDefault(brief => brief.NewWeapons.Contains(weapon))?.Level ?? 0)
                .Where(level => level > 0)
                .ToArray();

            text.AppendLine($"| {weapon} | {Format(model.WeaponPrices[weapon])} | {Format(Median(firsts))} | {firsts.Length} |");
        }

        text.AppendLine();
        
        int CountOf(LevelBrief brief, ItemCategory category) => brief.Pickups.Where(pair => pair.Key.PickupCategory == category).Sum(pair => pair.Value);
    }

    private static int? Median(IEnumerable<int> values)
    {
        int[] sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[sorted.Length / 2];
    }

    private static float? Median(IEnumerable<float> values)
    {
        float[] sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[sorted.Length / 2];
    }

    private static float? Median(DesignMeasures[] measures, Func<DesignMeasures, float?> get) => Median(measures.Select(get).OfType<float>());

    private static string GetFileName(string name) => Path.ChangeExtension(name, null).Replace('/', '_').Replace('\\', '_');

    private static string Format(MeasureShare share) => share.Total == 0 ? "-" : $"{Format(share.Count)} of {Format(share.Total)} ({MathF.Round(100 * share.Value!.Value)}%)";

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Format(int? value) => value is null ? "-" : Format(value.Value);

    private static string Format(float? value) => value is null ? "-" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatRange(int[] values) => values.Length == 0 ? "-" : values.Min() == values.Max() ? Format(values[0]) : $"{Format(values.Min())} to {Format(values.Max())}";
}

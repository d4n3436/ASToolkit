using System.Collections.Concurrent;
using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Reference;
using Xunit;

namespace ASToolkit.Tests.Generation;

[Trait("Category", "Integration")]
public class LevelGeneratorIntegrationTests
{
    private const int Seeds = 10;
    private const float HitPointSlack = 0.1f;
    private const float MonsterGap = 12;
    private const float HalfScreenWidth = 640;
    private const float HalfScreenHeight = 360;
    private const int MixedFamily = 4;
    private const float MostVariantShare = 0.75f;
    private const int LightestIntroduction = 2;
    private const float DecorFloorHeight = 5;
    private const float RoofHeight = 90;
    private const float BasementRoofShift = 17;
    private const float SpacingSlack = 1;

    private static readonly Lazy<(LevelGenerator Generator, ObjectTable Objects, ReferenceLevelSet References, StyleModel Model)> CampaignSources = new(() => CreateSources(GenerationStyle.Campaign));
    private static readonly Lazy<(LevelGenerator Generator, ObjectTable Objects, ReferenceLevelSet References, StyleModel Model)> AddOnSources = new(() => CreateSources(GenerationStyle.AddOn));
    private static readonly Lazy<IReadOnlyDictionary<(int Seed, int Level), GeneratedLevel>> CampaignLevels = new(() => GenerateAll(CampaignSources.Value.Generator, 10));
    private static readonly Lazy<IReadOnlyDictionary<(int Seed, int Level), GeneratedLevel>> AddOnLevels = new(() => GenerateAll(AddOnSources.Value.Generator, GenerationStyle.AddOn.LevelCount));

    [Fact]
    public void Generate_CampaignSeeds_ReachEveryObjective()
    {
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(CampaignLevels.Value, pair => FindFaults(pair.Value, $"seed {pair.Key.Seed} level {pair.Key.Level}", broken));

        Assert.Empty(broken);
    }

    [Fact]
    public void GenerateAddOn_Seeds_ReachEveryObjective()
    {
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(AddOnLevels.Value, pair => FindFaults(pair.Value, $"seed {pair.Key.Seed} add-on level {pair.Key.Level}", broken));

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_CampaignSeeds_PlaceDecorAsTheOriginals()
    {
        var (_, objects, _, model) = CampaignSources.Value;
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(CampaignLevels.Value.Where(pair => pair.Key.Level >= 2), pair => FindDecorFaults(pair.Value, $"seed {pair.Key.Seed} level {pair.Key.Level}", objects, model.Relations, broken));

        Assert.True(broken.IsEmpty, string.Join(Environment.NewLine, broken.Order()));
    }

    [Fact]
    public void GenerateAddOn_Seeds_PlaceDecorAsTheOriginals()
    {
        var (_, objects, _, model) = AddOnSources.Value;
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(AddOnLevels.Value.Where(pair => pair.Value.WallSet != WallSet.Outdoor), pair => FindDecorFaults(pair.Value, $"seed {pair.Key.Seed} add-on level {pair.Key.Level}", objects, model.Relations, broken));

        Assert.True(broken.IsEmpty, string.Join(Environment.NewLine, broken.Order()));
    }

    [Fact]
    public void Generate_CampaignSeeds_LeaveNoMoreRaisedPiecesUnsupportedThanTheCampaign()
    {
        var (_, objects, references, _) = CampaignSources.Value;
        var campaign = references.LevelNames.Where(name => !name.Contains('/') && name != "Level_01.map").Select(name => CountSupport(references.GetLevel(name).Map, objects)).ToArray();
        var generated = new ConcurrentBag<(int Raised, int Unsupported)>();
        Parallel.ForEach(CampaignLevels.Value.Where(pair => pair.Key.Level >= 2), pair => generated.Add(CountSupport(pair.Value.Map, objects)));

        float campaignShare = campaign.Sum(count => count.Unsupported) / (float)campaign.Sum(count => count.Raised);
        float generatedShare = generated.Sum(count => count.Unsupported) / (float)generated.Sum(count => count.Raised);
        Assert.InRange(generatedShare, 0, campaignShare);
    }

    [Fact]
    public void Generate_CampaignLabs_StandMonstersTriggersAndSitesInTheSecondWingSomeSitesInItsLockedRooms()
    {
        var broken = new List<string>();
        foreach (((int seed, int number), var level) in CampaignLevels.Value.Where(pair => pair.Value.Building == Building.UndergroundLab).OrderBy(pair => pair.Key))
        {
            var plan = level.Plan!;
            var regions = level.Map.Sprites
                .Where(sprite => sprite.Id == KnownObject.DynamiteSite)
                .Select(sprite => level.Layout!.Cells.TryGetValue(level.Layout.Lattice.GetCell(sprite.Position.X, sprite.Position.Y), out int region) ? plan.Regions[region] : null)
                .ToArray();

            if (regions.Any(region => region is null || !plan.SecondWing.Contains(region)))
            {
                broken.Add($"seed {seed} level {number}: a site outside the second wing");
            }

            var fighters = level.Map.Sprites
                .Where(sprite => KnownObject.Monsters.Contains(sprite.Id) || KnownObject.Bosses.Contains(sprite.Id) || sprite.Id == KnownObject.Spawner)
                .ToArray();

            var spots = fighters
                .Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y))
                .Concat(fighters.Select(sprite => sprite.Id == KnownObject.Spawner ? SpawnerProgram.Parse(sprite.Payload)?.Trigger : null)
                    .Where(trigger => trigger is not null)
                    .Select(trigger => new PointF(trigger!.Value.X, trigger.Value.Y)));

            if (spots.Any(spot => level.Layout!.Cells.TryGetValue(level.Layout.Lattice.GetCell(spot.X, spot.Y), out int region) && !plan.HoldsFights(plan.Regions[region])))
            {
                broken.Add($"seed {seed} level {number}: a monster or spawn trigger in the first wing");
            }

            if (regions.Length > 0 && plan.LockedSiteRooms.Count > 0 && !regions.Any(region => region is not null && plan.LockedSiteRooms.Contains(region)))
            {
                broken.Add($"seed {seed} level {number}: no site in a locked room");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_CampaignSeeds_StandSpawnTriggersWhereFightsStand()
    {
        var broken = new List<string>();
        foreach (((int seed, int number), var level) in CampaignLevels.Value.Where(pair => pair.Value.Plan is not null).OrderBy(pair => pair.Key))
        {
            var triggers = level.Map.Sprites
                .Where(sprite => sprite.Id == KnownObject.Spawner)
                .Select(sprite => SpawnerProgram.Parse(sprite.Payload)?.Trigger)
                .Where(trigger => trigger is not null)
                .Select(trigger => level.Layout!.Lattice.GetCell(trigger!.Value.X, trigger.Value.Y));

            if (triggers.Any(cell => level.Layout!.Cells.TryGetValue(cell, out int region) && !level.Plan!.HoldsFights(level.Plan.Regions[region])))
            {
                broken.Add($"seed {seed} level {number}: a spawn trigger where no fight may stand");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_NeverReleaseAnAmbushFromAnEarlierPhaseThanItsTrigger()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var _, var _) in GetIndoorLevels())
        {
            var plan = level.Plan!;
            var layout = level.Layout!;
            int behind = level.Map.Sprites
                .Where(sprite => sprite.Id == KnownObject.Spawner)
                .Select(sprite => (Spawner: layout.Lattice.GetCell(sprite.Position.X, sprite.Position.Y), SpawnerProgram.Parse(sprite.Payload)?.Trigger))
                .Where(pair => pair.Trigger is not null)
                .Select(pair => (Spawner: layout.Cells.GetValueOrDefault(pair.Spawner, -1), Trigger: layout.Cells.GetValueOrDefault(layout.Lattice.GetCell(pair.Trigger!.Value.X, pair.Trigger.Value.Y), -1)))
                .Count(pair => pair.Spawner >= 0 && pair.Trigger >= 0 && plan.Regions[pair.Spawner].Kind != RegionKind.SpawnCloset
                               && plan.Regions[pair.Spawner].Phase < plan.Regions[pair.Trigger].Phase);

            if (behind > 0)
            {
                broken.Add($"{style} seed {seed} level {number}: {behind} spawners in an earlier phase than their triggers");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_StandSpawnersOffTheScreenAroundTheirTriggers()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var _, var _) in GetIndoorLevels())
        {
            int seen = level.Map.Sprites
                .Where(sprite => sprite.Id == KnownObject.Spawner)
                .Select(sprite => (sprite.Position, SpawnerProgram.Parse(sprite.Payload)?.Trigger))
                .Where(pair => pair.Trigger is not null && level.Plan!.Regions[level.Layout!.Cells.GetValueOrDefault(level.Layout.Lattice.GetCell(pair.Position.X, pair.Position.Y), 0)].Kind != RegionKind.SpawnCloset)
                .Count(pair => Math.Abs(pair.Position.X - pair.Trigger!.Value.X) < HalfScreenWidth + pair.Trigger.Value.Radius
                               && Math.Abs(pair.Position.Y - pair.Trigger.Value.Y) < HalfScreenHeight + pair.Trigger.Value.Radius);

            if (seen > 0)
            {
                broken.Add($"{style} seed {seed} level {number}: {seen} spawners on the 1280x720 screen of a player inside their trigger");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_StandEverySpawnTriggerWhereEveryWalkToTheEndCrossesIt()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var _, var _) in GetIndoorLevels())
        {
            var plan = level.Plan!;
            var layout = level.Layout!;
            var triggers = level.Map.Sprites
                .Select(sprite => sprite.Id == KnownObject.Spawner ? SpawnerProgram.Parse(sprite.Payload)?.Trigger : null)
                .Where(trigger => trigger is not null)
                .Select(trigger => trigger!.Value)
                .Distinct()
                .ToArray();

            var start = layout.Cells.Where(pair => pair.Value == plan.Stations[0].Id).Select(pair => pair.Key);
            var end = layout.Cells.Where(pair => pair.Value == plan.Stations[^1].Id).Select(pair => pair.Key).ToHashSet();
            int dodged = triggers.Count(trigger => WalksAround(layout, start, end, cell =>
            {
                var center = layout.Lattice.GetCenter(cell);
                return Distance(center, new PointF(trigger.X, trigger.Y)) < trigger.Radius;
            }));

            if (dodged > 0)
            {
                broken.Add($"{style} seed {seed} level {number}: {dodged} spawn triggers a walk to the end can go around");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_MixTheVariantsOfAMonsterFamilyInEveryFight()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var model, var _) in GetIndoorLevels())
        {
            var held = model.Campaign.Monsters.Where(kind => kind.FirstLevel == number).Select(kind => kind.Id.MonsterFamily).ToHashSet();
            var offered = level.Fights
                .SelectMany(fight => fight.Monsters)
                .Distinct()
                .GroupBy(id => id.MonsterFamily)
                .Where(family => family.Count() > 1 && !held.Contains(family.Key))
                .Select(family => family.Key)
                .ToHashSet();

            int alike = level.Fights.Count(fight => fight.Monsters
                .GroupBy(id => id.MonsterFamily)
                .Any(family => offered.Contains(family.Key) && family.Count() >= MixedFamily && family.GroupBy(id => id).Max(variant => variant.Count()) > MostVariantShare * family.Count() + 1));

            if (alike > 0)
            {
                broken.Add($"{style} seed {seed} level {number}: {alike} fights holding one variant of a family over three quarters of its monsters");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_PlaceEachPhasesPlannedHitPointsInStandingMonstersAndSpawners()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var _, var objects) in GetIndoorLevels())
        {
            var plan = level.Plan!;
            var layout = level.Layout!;
            int GetHitPoints(KnownObject id) => id.IsEndlessGenerator || !objects.TryFind(id, out var found) ? 0 : found.MaxHp;
            int GetPhase(float x, float y) => layout.Cells.TryGetValue(layout.Lattice.GetCell(x, y), out int region) ? plan.Regions[region].Phase : -1;
            var planned = level.Fights
                .GroupBy(fight => plan.Regions[fight.Region].Phase)
                .ToDictionary(phase => phase.Key, phase => phase.Sum(fight => fight.HitPoints));

            var kinds = level.Fights.SelectMany(fight => fight.Monsters).ToHashSet();
            var placed = level.Map.Sprites
                .Where(sprite => kinds.Contains(sprite.Id))
                .Select(sprite => (Phase: GetPhase(sprite.Position.X, sprite.Position.Y), HitPoints: GetHitPoints(sprite.Id)))
                .Concat(level.Map.Sprites
                    .Where(sprite => sprite.Id == KnownObject.Spawner)
                    .Select(sprite => SpawnerProgram.Parse(sprite.Payload))
                    .Where(program => program?.Trigger is not null)
                    .Select(program => (Phase: GetPhase(program!.Trigger!.Value.X, program.Trigger.Value.Y), HitPoints: program.Monsters.Sum(GetHitPoints))))
                .GroupBy(pair => pair.Phase)
                .ToDictionary(phase => phase.Key, phase => phase.Sum(pair => pair.HitPoints));

            int target = level.Budget!.TargetHitPoints;
            int total = planned.Values.Sum();
            if (Math.Abs(total - target) > HitPointSlack * target)
            {
                broken.Add($"{style} seed {seed} level {number}: target {target}, planned {total}");
            }

            foreach (var (phase, hitPoints) in planned.Where(pair => Math.Abs(placed.GetValueOrDefault(pair.Key) - pair.Value) > HitPointSlack * pair.Value))
            {
                broken.Add($"{style} seed {seed} level {number} phase {phase}: planned {hitPoints}, placed {placed.GetValueOrDefault(phase)}");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_StandMonstersTwelvePixelsApart()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var _, var _) in GetIndoorLevels())
        {
            var kinds = level.Fights.SelectMany(fight => fight.Monsters).ToHashSet();
            var monsters = level.Map.Sprites
                .Where(sprite => kinds.Contains(sprite.Id))
                .Select(sprite => new PointF(sprite.Position.X, sprite.Position.Y))
                .ToArray();

            int close = monsters.Select((monster, index) => monsters.Skip(index + 1).Count(other => Distance(monster, other) < MonsterGap)).Sum();
            if (close > 0)
            {
                broken.Add($"{style} seed {seed} level {number}: {close} monster pairs under {MonsterGap} px apart");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_BothStyles_IntroduceEachMonsterKindInAFightNoHarderThanTheLevelsMedian()
    {
        var broken = new List<string>();
        foreach ((string style, int seed, int number, var level, var model, var objects) in GetIndoorLevels())
        {
            var firstLevels = model.Campaign.Monsters.ToDictionary(kind => kind.Id, kind => kind.FirstLevel);
            var fights = level.Fights.OrderBy(fight => fight.Order).ToArray();
            float median = fights.Select(fight => (float)fight.HitPoints).Order().ElementAtOrDefault(fights.Length / 2);
            foreach (var kind in fights.SelectMany(fight => fight.Monsters).Distinct().Where(kind => firstLevels.GetValueOrDefault(kind) == number && number > firstLevels.Values.Min()))
            {
                var first = fights.First(fight => fight.Monsters.Contains(kind));
                int single = objects.TryFind(kind, out var found) ? found.MaxHp : 0;
                if (first.IsAmbush || first.HitPoints > median + LightestIntroduction * single)
                {
                    broken.Add($"{style} seed {seed} level {number}: {kind} first in {(first.IsAmbush ? "an ambush" : "a group")} of {first.HitPoints} hit points, the median {median}");
                }
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_CampaignBlackouts_StandTheTriggerInsideTheEntranceAwayFromItsDoor()
    {
        var broken = new List<string>();
        foreach (((int seed, int number), var level) in CampaignLevels.Value.Where(pair => pair.Value.Script.Blackout is not null).OrderBy(pair => pair.Key))
        {
            var plan = level.Plan!;
            var layout = level.Layout!;
            var room = plan.TriggerRoom;
            var link = plan.BlackoutLink;
            if (room is null || link is null)
            {
                broken.Add($"seed {seed} level {number}: no trigger room");
                continue;
            }

            var door = layout.Lattice.GetCenter(layout.GetDoorwayCell(link.Id, link.To));
            (var point, int radius) = level.Script.Blackout!.Value;
            bool inside = layout.Cells.TryGetValue(layout.Lattice.GetCell(point.X, point.Y), out int region) && region == room.Id;
            float distance = MathF.Sqrt((point.X - door.X) * (point.X - door.X) + (point.Y - door.Y) * (point.Y - door.Y));
            if (!inside || distance <= radius)
            {
                broken.Add($"seed {seed} level {number}: trigger {(inside ? "reaching its door" : "outside the trigger room")}");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_SameSeedTwice_WritesTheSameFiles()
    {
        var generator = CampaignSources.Value.Generator;
        string folder = Directory.CreateTempSubdirectory().FullName;
        string first = Path.Combine(folder, "a.map");
        string second = Path.Combine(folder, "b.map");
        var level = generator.Generate(3, 6);
        level.Write(first, generator.GetTemplatePath(level));
        generator.Generate(3, 6).Write(second, generator.GetTemplatePath(level));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(first, ".lgc")), File.ReadAllBytes(Path.ChangeExtension(second, ".lgc")));
    }

    private static void FindFaults(GeneratedLevel level, string name, ConcurrentBag<string> faults)
    {
        if (level.Provenance.Values.Sum() != level.Map.Sprites.Count)
        {
            faults.Add($"{name}: provenance of {level.Provenance.Values.Sum()} sprites for {level.Map.Sprites.Count}");
        }

        var analysis = level.Analysis;
        if (analysis is null)
        {
            faults.Add($"{name}: no route");
            return;
        }

        if (analysis.FallbackUsage is { ForcedGates: true } or { Unreached: > 0 } || level.WallSet != WallSet.Outdoor && analysis.FallbackUsage.UsedSecretWalls)
        {
            faults.Add($"{name}: {analysis.FallbackUsage}");
        }

        foreach (var sprite in level.Map.Sprites.Where(sprite => sprite.Payload.Length < 8))
        {
            faults.Add($"{name}: {sprite.Id} at ({sprite.Position.X:0}, {sprite.Position.Y:0}) has {sprite.Payload.Length} payload bytes");
        }

        var layout = level.Layout;
        if (layout is not null)
        {
            foreach (var sprite in level.Map.Sprites.Where(sprite => sprite.Id.IsDecor && sprite.Position.Z < DecorFloorHeight
                         && !layout.Cells.ContainsKey(layout.Lattice.GetCell(sprite.Position.X, sprite.Position.Y))))
            {
                faults.Add($"{name}: {sprite.Id} at ({sprite.Position.X:0}, {sprite.Position.Y:0}) stands off the floor");
            }
        }

        var plan = level.Plan;
        if (plan is { Gate: GateKind.Fences } && layout is not null)
        {
            var held = level.Map.Sprites
                .Where(sprite => sprite.Id == KnownObject.Generator)
                .Select(sprite => layout.Cells.GetValueOrDefault(layout.Lattice.GetCell(sprite.Position.X, sprite.Position.Y), -1))
                .ToHashSet();

            foreach (var (room, _) in plan.GeneratorRooms.Where(entry => !held.Contains(entry.Room.Id)))
            {
                faults.Add($"{name}: generator room {room.Id} holds no generator");
            }
        }

        int sites = analysis.Elements.Count(element => element.Kind == ElementKind.DynamiteSite);
        int charges = analysis.Elements.Count(element => element.Id == KnownObject.Dynamite);
        if (level.AddOnNumber is null && level.Level > 1 && charges != sites)
        {
            faults.Add($"{name}: {charges} charges for {sites} sites");
        }
    }

    private static void FindDecorFaults(GeneratedLevel level, string name, ObjectTable objects, ObjectRelationTable relations, ConcurrentBag<string> faults)
    {
        var plan = level.Plan!;
        var layout = level.Layout!;
        var lattice = layout.Lattice;
        var decor = level.Map.Sprites.Where(sprite => sprite.Id.IsDecor).ToArray();
        var blocking = decor.Where(sprite => objects.GetBlocking(sprite.Id) != BlockingClass.WalkThrough).ToArray();
        var floor = blocking.Where(sprite => sprite.Position.Z < DecorFloorHeight).ToArray();
        var doorways = plan.Links
            .Select((link, index) => (link.Kind, Edges: layout.LinkEdges[index]))
            .Where(link => link.Kind is LinkKind.Door or LinkKind.Arch or LinkKind.Fence)
            .SelectMany(link => link.Edges)
            .Select(edge => edge.Axis == 'v'
                ? new MapSegment(lattice.ToWorld(edge.A, edge.B), lattice.ToWorld(edge.A + 1, edge.B))
                : new MapSegment(lattice.ToWorld(edge.A, edge.B), lattice.ToWorld(edge.A, edge.B + 1)))
            .ToArray();

        foreach (var sprite in floor.Concat(decor.Where(sprite => sprite.Id.IsWallMounted)))
        {
            var blockingClass = sprite.Id.IsWallMounted ? BlockingClass.Hittable : objects.GetBlocking(sprite.Id);
            float? clearance = relations.GetDoorClearance(level.WallSet, blockingClass) - SpacingSlack;
            if (clearance is not null && doorways.Any(doorway => doorway.MeasureDistance(new PointF(sprite.Position.X, sprite.Position.Y)) < clearance))
            {
                faults.Add($"{name}: {sprite.Id} at ({sprite.Position.X:0}, {sprite.Position.Y:0}) stands within {clearance:0} px of a doorway");
            }
        }

        foreach (var sprite in blocking)
        {
            objects.TryFind(sprite.Id, out var obj);
            bool raised = sprite.Position.Z >= DecorFloorHeight;
            foreach (var other in blocking.Where(other => !ReferenceEquals(other, sprite) && other.Position.Z >= DecorFloorHeight == raised))
            {
                objects.TryFind(other.Id, out var otherObj);
                float spacing = relations.GetSpacing(level.WallSet, sprite.Id, other.Id, raised);
                if (Math.Abs(other.Position.X - sprite.Position.X) < spacing * (obj.Size.X + otherObj.Size.X) - SpacingSlack
                    && Math.Abs(other.Position.Y - sprite.Position.Y) < spacing * (obj.Size.Y + otherObj.Size.Y) - SpacingSlack)
                {
                    faults.Add($"{name}: {sprite.Id} at ({sprite.Position.X:0}, {sprite.Position.Y:0}) stands inside {other.Id}");
                    break;
                }
            }
        }

        foreach (var pickup in level.Map.Sprites.Where(sprite => sprite.Id.IsPickup))
        {
            var under = floor.FirstOrDefault(sprite => objects.TryFind(sprite.Id, out var obj)
                && CollisionBox.Create(obj, sprite.Position.X, sprite.Position.Y, CollisionBox.ExtentShare, 0).Contains(pickup.Position.X, pickup.Position.Y));
            if (under is not null)
            {
                faults.Add($"{name}: {pickup.Id} at ({pickup.Position.X:0}, {pickup.Position.Y:0}) lies under {under.Id}");
            }
        }

        float shift = level.WallSet == WallSet.Basement ? BasementRoofShift : 0;
        foreach (var roof in level.Map.Sprites.Where(sprite => sprite is { Id: KnownObject.Earth, Position.Z: >= RoofHeight }))
        {
            if (layout.Cells.TryGetValue(lattice.GetCell(roof.Position.X, roof.Position.Y - shift), out int region)
                && plan.Regions[region].Kind is not (RegionKind.SecretCloset or RegionKind.SpawnCloset))
            {
                faults.Add($"{name}: roof at ({roof.Position.X:0}, {roof.Position.Y:0}) covers floor");
            }
        }
    }

    private static (int Raised, int Unsupported) CountSupport(MapResourceBuilder map, ObjectTable objects)
    {
        var decor = map.Sprites.Where(sprite => sprite.Id.IsDecor).ToArray();
        int raised = 0;
        int unsupported = 0;
        for (int index = 0; index < decor.Length; index++)
        {
            var sprite = decor[index];
            if (sprite.Id.Family is null || sprite.Id.IsWallMounted || sprite.Position.Z < DecorFloorHeight)
            {
                continue;
            }

            raised++;
            if (objects.FindSupport(decor, index) == -1)
            {
                unsupported++;
            }
        }

        return (raised, unsupported);
    }

    private static IEnumerable<(string Style, int Seed, int Number, GeneratedLevel Level, StyleModel Model, ObjectTable Objects)> GetIndoorLevels() =>
        new[] { ("campaign", CampaignSources.Value, CampaignLevels.Value), ("add-on", AddOnSources.Value, AddOnLevels.Value) }
            .SelectMany(style => style.Item3
                .Where(pair => pair.Value.Plan is not null)
                .OrderBy(pair => pair.Key)
                .Select(pair => (style.Item1, pair.Key.Seed, pair.Key.Level, pair.Value, style.Item2.Model, style.Item2.Objects)));

    private static bool WalksAround(ASToolkit.Core.Generation.Geometry.FineLayout layout, IEnumerable<Cell> start, HashSet<Cell> end, Func<Cell, bool> avoided)
    {
        var queue = new Queue<Cell>(start.Where(cell => !avoided(cell)));
        var seen = queue.ToHashSet();
        while (queue.TryDequeue(out var cell))
        {
            if (end.Contains(cell))
            {
                return true;
            }

            foreach (var (next, edge) in EdgeKey.GetNeighbors(cell))
            {
                bool closed = layout.Edges.TryGetValue(edge, out var kind) && (kind.BlocksWalking || kind is EdgeKind.SecretWall or EdgeKind.SpawnWall);
                if (layout.Cells.ContainsKey(next) && !closed && !avoided(next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static IReadOnlyDictionary<(int Seed, int Level), GeneratedLevel> GenerateAll(LevelGenerator generator, int levels)
    {
        var jobs = Enumerable.Range(1, Seeds).SelectMany(seed => Enumerable.Range(1, levels).Select(level => (Seed: seed, Level: level))).ToArray();
        var generated = new ConcurrentDictionary<(int Seed, int Level), GeneratedLevel>();
        Parallel.ForEach(jobs, job => generated[job] = generator.Generate(job.Seed, job.Level));
        return generated;
    }

    private static (LevelGenerator Generator, ObjectTable Objects, ReferenceLevelSet References, StyleModel Model) CreateSources(GenerationStyle style)
    {
        string game = TestGameFolder.RequireFolder();
        var objects = TestGameFolder.Objects;
        var references = TestGameFolder.References;
        var model = new StyleFitter(objects, GameTable.Load(Path.Combine(game, "Maps", "common_table.lgc"))).Fit(references, style);
        return (new LevelGenerator(game, objects, references, model), objects, references, model);
    }
}

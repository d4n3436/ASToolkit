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
    private const float DecorFloorHeight = 5;
    private const float RoofHeight = 90;
    private const float LabRoofShift = 17;
    private const float SpacingSlack = 1;

    [Fact]
    public void Generate_CampaignSeeds_ReachEveryObjective()
    {
        var generator = CreateGenerator();
        var jobs = Enumerable.Range(1, Seeds).SelectMany(seed => Enumerable.Range(1, 10).Select(level => (Seed: seed, Level: level))).ToArray();
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(jobs, job => FindFaults(generator.Generate(job.Seed, job.Level), $"seed {job.Seed} level {job.Level}", broken));

        Assert.Empty(broken);
    }

    [Fact]
    public void GenerateAddOn_Seeds_ReachEveryObjective()
    {
        var generator = CreateGenerator(addOnStyle: true);
        var jobs = Enumerable.Range(1, Seeds).SelectMany(seed => Enumerable.Range(1, LevelGenerator.AddOnLevelCount).Select(level => (Seed: seed, Level: level))).ToArray();
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(jobs, job => FindFaults(generator.GenerateAddOn(job.Seed, job.Level), $"seed {job.Seed} add-on level {job.Level}", broken));

        Assert.Empty(broken);
    }

    [Fact]
    public void Generate_CampaignSeeds_PlaceDecorAsTheOriginals()
    {
        var (generator, objects, references) = CreateSources(addOnStyle: false);
        var jobs = Enumerable.Range(1, Seeds).SelectMany(seed => Enumerable.Range(2, 9).Select(level => (Seed: seed, Level: level))).ToArray();
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(jobs, job => FindDecorFaults(generator.Generate(job.Seed, job.Level), $"seed {job.Seed} level {job.Level}", objects, references.Relations, broken));

        Assert.True(broken.IsEmpty, string.Join(Environment.NewLine, broken.Order()));
    }

    [Fact]
    public void GenerateAddOn_Seeds_PlaceDecorAsTheOriginals()
    {
        var (generator, objects, references) = CreateSources(addOnStyle: true);
        var jobs = Enumerable.Range(1, Seeds).SelectMany(seed => Enumerable.Range(1, LevelGenerator.AddOnLevelCount).Select(level => (Seed: seed, Level: level))).ToArray();
        var broken = new ConcurrentBag<string>();
        Parallel.ForEach(jobs, job =>
        {
            var level = generator.GenerateAddOn(job.Seed, job.Level);
            if (level.Theme != LevelTheme.Outdoor)
            {
                FindDecorFaults(level, $"seed {job.Seed} add-on level {job.Level}", objects, references.Relations, broken);
            }
        });

        Assert.True(broken.IsEmpty, string.Join(Environment.NewLine, broken.Order()));
    }

    [Fact]
    public void Generate_CampaignSeeds_LeaveNoMoreRaisedPiecesUnsupportedThanTheCampaign()
    {
        var (generator, objects, references) = CreateSources(addOnStyle: false);
        var campaign = references.LevelNames.Where(name => !name.Contains('/') && name != "Level_01.map").Select(name => CountSupport(references.GetLevel(name).Map, objects)).ToArray();
        var jobs = Enumerable.Range(1, Seeds).SelectMany(seed => Enumerable.Range(2, 9).Select(level => (Seed: seed, Level: level))).ToArray();
        var generated = new ConcurrentBag<(int Raised, int Unsupported)>();
        Parallel.ForEach(jobs, job => generated.Add(CountSupport(generator.Generate(job.Seed, job.Level).Map, objects)));

        float campaignShare = campaign.Sum(count => count.Unsupported) / (float)campaign.Sum(count => count.Raised);
        float generatedShare = generated.Sum(count => count.Unsupported) / (float)generated.Sum(count => count.Raised);
        Assert.InRange(generatedShare, 0, campaignShare);
    }

    [Fact]
    public void Generate_SameSeedTwice_WritesTheSameFiles()
    {
        var generator = CreateGenerator();
        string folder = Directory.CreateTempSubdirectory().FullName;
        string first = Path.Combine(folder, "a.map");
        string second = Path.Combine(folder, "b.map");
        generator.Generate(3, 6).Write(first, generator.GetTemplatePath(6));
        generator.Generate(3, 6).Write(second, generator.GetTemplatePath(6));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(first, ".lgc")), File.ReadAllBytes(Path.ChangeExtension(second, ".lgc")));
    }

    private static void FindFaults(GeneratedLevel level, string name, ConcurrentBag<string> faults)
    {
        var analysis = level.Analysis;
        if (analysis is null)
        {
            faults.Add($"{name}: no route");
            return;
        }

        if (analysis.FallbackUsage is { ForcedGates: true } or { Unreached: > 0 } || level.Theme != LevelTheme.Outdoor && analysis.FallbackUsage.UsedSecretWalls)
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
            float? clearance = relations.GetDoorClearance(level.Theme, blockingClass) - SpacingSlack;
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
                float spacing = relations.GetSpacing(level.Theme, sprite.Id, other.Id, raised);
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

        float shift = level.Theme == LevelTheme.Lab ? LabRoofShift : 0;
        foreach (var roof in level.Map.Sprites.Where(sprite => sprite.Id == KnownObject.Earth && sprite.Position.Z >= RoofHeight))
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

    private static LevelGenerator CreateGenerator(bool addOnStyle = false) => CreateSources(addOnStyle).Generator;

    private static (LevelGenerator Generator, ObjectTable Objects, ReferenceLevelSet References) CreateSources(bool addOnStyle)
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = ReferenceLevelSet.Load(game, objects, addOnStyle);
        return (new LevelGenerator(game, objects, references, addOnStyle), objects, references);
    }
}

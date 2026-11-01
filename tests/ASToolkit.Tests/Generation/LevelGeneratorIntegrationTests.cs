using System.Collections.Concurrent;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation;
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

    private static readonly string[] ArmySources = ["Level_02.map", "Level_04.map", "Level_07.map", "ADDON/Level_02.map", "ADDON2/Level_04.map"];

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

    private static LevelGenerator CreateGenerator(bool addOnStyle = false)
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        return new LevelGenerator(game, objects, ReferenceLevelSet.Load(game, objects, addOnStyle), ArmyTable.Load(game, ArmySources), addOnStyle);
    }
}

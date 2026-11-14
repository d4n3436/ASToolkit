using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Simulation;

public class CampaignTests
{
    [Fact]
    public void PlayCampaign_CarriesEquipmentAndEarningsThroughTheShop()
    {
        string game = TestGameFolder.RequireFolder();
        var play = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table);
        var first = EmptyLevel(KnownObject.Money, KnownObject.Medkit, KnownObject.Flashlight);
        var second = EmptyLevel();
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int> { [KnownObject.Shotgun] = 0 }, new PlayerImplants(1, 0, 0, 0));
        var result = play.PlayCampaign([first, second], [100, 200], 1, new CampaignState(0, loadout), record: true, recordedLevel: 1);

        Assert.True(result.IsCompleted);
        Assert.Equal(2, result.Levels.Count);
        Assert.Equal(0, result.Levels[0].Ammo[KnownObject.Shotgun]);
        var carried = result.Levels[1].StartingLoadout!;
        Assert.Equal(70, carried.Ammo[KnownObject.Shotgun]);
        Assert.Equal(loadout.Implants, carried.Implants);
        Assert.Equal(1, carried.Items[KnownObject.Medkit]);
        Assert.Equal(1, carried.Items[KnownObject.Flashlight]);
        Assert.Equal(210, result.State.Money);
        Assert.Equal(carried.Items, result.State.Loadout.Items);
        Assert.Null(result.Levels[0].Replay);
        Assert.NotEmpty(result.Levels[1].Replay!.Frames);
    }

    [Fact]
    public void PlayCampaign_OriginalLevels_OpensAmbushWallsAndBuildsWavesAfterLevelTwo()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = TestGameFolder.Objects;
        var levels = ((int[])[2, 3]).Select(number =>
        {
            string name = $"Level_{number:00}.map";
            string path = Path.Combine(game, "Maps", name);

            return RouteAnalyzer.Analyze(name, MapResourceBuilder.Load(path), LevelScriptReader.Read(Path.ChangeExtension(path, ".lgc")), objects)!;
        }).ToArray();
        var table = TestGameFolder.Table;
        var rewards = table.GetArray("LevelMoney");
        var state = new CampaignState(rewards[0] + rewards[1], new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default));
        var result = new Playthrough(game, objects, table).PlayCampaign(levels, rewards.Skip(2).ToArray(), 1, state, record: true, recordedLevel: 1);

        Assert.True(result.IsCompleted);
        Assert.Contains(result.Levels[1].Replay!.Spawns, spawn => spawn.Source is not null && spawn.Source.Id == KnownObject.Spawner);
        Assert.Contains(result.Levels[1].Replay!.RemovedSprites, removal => removal.Placement.Id == KnownObject.SpawnWallSlash);
        Assert.Contains(result.Levels[1].Replay!.RemovedSprites, removal => removal.Placement.Id == KnownObject.SpawnWallBack);
    }

    private static RouteAnalysis EmptyLevel(params KnownObject[] pickups)
    {
        var lattice = new Lattice(20, 14, 200, 400);
        var tour = Enumerable.Range(0, 16).Select(index => new Cell(index, 0)).ToArray();
        var cells = tour.ToHashSet();
        var start = lattice.GetCenter(tour[0]);
        var elements = pickups.Select(id => new RouteElement
        {
            Kind = ElementKind.Pickup, Id = id, X = (int)start.X, Y = (int)start.Y, Cell = tour[0], Hidden = false, Facing = 0
        }).ToArray();

        return new RouteAnalysis
        {
            Name = "Campaign test", WallSet = WallSet.Basement,
            Model = new LevelModel
            {
                Name = "Campaign test", WallSet = WallSet.Basement, Lattice = lattice, Cells = cells, Edges = new Dictionary<EdgeKey, EdgeKind>(), Objects = [], Sprites = []
            },
            Step = lattice.CellStep, ReachableCells = cells.Count, RoutePixels = (int)(lattice.CellStep * (tour.Length - 1)), SpinePixels = 0,
            RevisitShare = 0, Legs = [], Objectives = [], Elements = elements, Gating = new GateMeasurement(), Checkpoints = [],
            FallbackUsage = new FallbackUsage(false, false, 0), Tour = tour,
            Grid = new RouteGrid { Lattice = lattice, Cells = cells, Edges = [], FenceEdges = [], BlockedCells = [] }
        };
    }
}

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
        Assert.NotEmpty(result.Levels[1].Replay!.Inputs);
    }

    [Fact]
    public void Run_LevelThree_OpensAmbushWallsAndBuildsWaves()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = TestGameFolder.Objects;
        string path = Path.Combine(game, "Maps", "Level_03.map");
        var level = RouteAnalyzer.Analyze("Level_03.map", MapResourceBuilder.Load(path), LevelScriptReader.Read(Path.ChangeExtension(path, ".lgc"), objects.ScriptVersion), objects)!;
        var loadout = new PlayerLoadout(KnownObject.RedArmor, new Dictionary<KnownObject, int> { [KnownObject.Minigun] = 800, [KnownObject.RocketLauncher] = 80 },
            new PlayerImplants(3, 3, 3, 3));
        var recorder = new ReplayRecorder();
        var session = new Playthrough(game, objects, TestGameFolder.Table).Start(level, loadout, 1, recorder);
        int placed = level.Elements.Count(element => element.Kind == ElementKind.Monster);
        
        for (int time = 10000; time <= 240000 && !(session.Log.Kills > placed && Opened(KnownObject.SpawnWallSlash) && Opened(KnownObject.SpawnWallBack)); time += 10000)
        {
            session.PlayUntil(time, recorder);
        }

        Assert.True(session.Log.Kills > placed);
        Assert.True(Opened(KnownObject.SpawnWallSlash));
        Assert.True(Opened(KnownObject.SpawnWallBack));
        return;
        
        bool Opened(KnownObject id) => recorder.CreateReplay([]).RemovedSprites.Any(removal => removal.Placement.Id == id);
    }

    [Fact]
    public void Start_AccuracyImplants_NarrowTheWeaponAimThroughTheItemScript()
    {
        const int accuracyImplants = 2;
        
        string game = TestGameFolder.RequireFolder();
        var play = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table);
        var level = EmptyLevel();

        var weapons = Enumerable.Range(Unit.WeaponIdBase, Unit.WeaponSlots).Select(id => (KnownObject)id).ToArray();
        var plain = play.Start(level, new PlayerLoadout(0, new Dictionary<KnownObject, int>(), new PlayerImplants(0, 0, 0, 0)), 1, null);
        plain.PlayUntil(100);
        int[] aims = weapons.Select(id => (int)plain.World.Definitions.Find(id)!.Aim).ToArray();
        var implanted = play.Start(level, new PlayerLoadout(0, new Dictionary<KnownObject, int>(), new PlayerImplants(0, 0, 0, accuracyImplants)), 1, null);
        implanted.PlayUntil(100);

        Assert.Contains(aims, aim => aim > 0);
        Assert.Equal(aims.Select(aim => Enumerable.Range(0, accuracyImplants).Aggregate(aim, (scaled, _) => scaled * 6 / 10)), weapons.Select(id => (int)implanted.World.Definitions.Find(id)!.Aim));
    }

    private static RouteAnalysis EmptyLevel(params KnownObject[] pickups)
    {
        var lattice = new Lattice(20, 14, 200, 400);
        var tour = Enumerable.Range(0, 16).Select(index => new Cell(index, 0)).ToArray();
        var cells = tour.ToHashSet();
        var start = lattice.GetCenter(tour[0]);
        var elements = pickups.Select(id => new RouteElement
        {
            Kind = ElementKind.Pickup, Id = id, X = (int)start.X, Y = (int)start.Y, Cell = tour[0], Hidden = false, Direction = 0
        }).ToArray();

        return new RouteAnalysis
        {
            Name = "Campaign test", WallSet = WallSet.Basement,
            Model = new LevelModel
            {
                Name = "Campaign test", WallSet = WallSet.Basement, Lattice = lattice, Cells = cells, Edges = new Dictionary<EdgeKey, EdgeKind>(), Objects = [], Sprites = [], StoredSprites = []
            },
            Step = lattice.CellStep, ReachableCells = cells.Count, RoutePixels = (int)(lattice.CellStep * (tour.Length - 1)), SpinePixels = 0,
            RevisitShare = 0, Legs = [], Objectives = [], Elements = elements, Gating = new GateMeasurement(), Checkpoints = [],
            FallbackUsage = new FallbackUsage(false, false, 0), Tour = tour,
            Grid = new RouteGrid { Lattice = lattice, Cells = cells, Edges = [], FenceEdges = [], BlockedCells = [] }
        };
    }
}

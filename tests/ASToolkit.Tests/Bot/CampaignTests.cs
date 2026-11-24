using System.Drawing;
using System.Numerics;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Routes;
using ASToolkit.Analysis.Scripting;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Bot;

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
        Assert.Null(result.Levels[0].Demo);
        Assert.NotNull(result.Levels[1].Demo);
    }

    [Fact]
    public void LoadReplay_RecordedRun_PlaysTheSameFramesFromItsDemo()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = TestGameFolder.Objects;
        string path = Path.Combine(game, "Maps", "Level_02.map");
        var level = RouteAnalyzer.Analyze("Level_02.map", MapResourceBuilder.Load(path), LevelScriptReader.Read(Path.ChangeExtension(path, ".lgc"), objects.ScriptVersion), objects)!;
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);
        var playthrough = new Playthrough(game, objects, TestGameFolder.Table);
        var session = playthrough.Start(level, loadout, 1, record: true);
        session.PlayUntil(60000);
        var recorded = session.Map;

        var replay = playthrough.LoadReplay(level, loadout, new DemoResource(recorded.FinishDemoRecording()));
        uint now = 0;
        int frames = 0;
        while (replay.TryGetNextDemoTime(out int next))
        {
            if (replay.StepAimed(replay.WallTime + (uint)(next - replay.Time), now, out now) || !replay.IsPlayingDemo)
                break;

            frames++;
        }

        Assert.True(frames > 1000);
        Assert.Equal(recorded.RandomState, replay.RandomState);
        Assert.Equal(recorded.Player.Position, replay.Player.Position);
        Assert.Equal(recorded.Player.Hp, replay.Player.Hp);
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
        var session = new Playthrough(game, objects, TestGameFolder.Table).Start(level, loadout, 1);
        int placed = level.Elements.Count(element => element.Kind == ElementKind.Monster);
        var walls = session.Map.UpdatedSprites.Where(sprite => sprite.Vid.Id is KnownObject.SpawnWallSlash or KnownObject.SpawnWallBack).ToArray();

        for (int time = 10000; time <= 240000 && !(session.Log.Kills > placed && Opened(KnownObject.SpawnWallSlash) && Opened(KnownObject.SpawnWallBack)); time += 10000)
        {
            session.PlayUntil(time);
        }

        Assert.True(session.Log.Kills > placed);
        Assert.True(Opened(KnownObject.SpawnWallSlash));
        Assert.True(Opened(KnownObject.SpawnWallBack));
        return;
        
        bool Opened(KnownObject id) => walls.Any(wall => wall.Vid.Id == id && (wall.Animation >= SpriteAnimation.Death || wall.ReferenceCount <= 0));
    }

    [Fact]
    public void Start_AccuracyImplants_NarrowTheWeaponAimThroughTheItemScript()
    {
        const int accuracyImplants = 2;
        
        string game = TestGameFolder.RequireFolder();
        var play = new Playthrough(game, TestGameFolder.Objects, TestGameFolder.Table);
        var level = EmptyLevel();

        var weapons = Enumerable.Range(Sprite.WeaponIdBase, Sprite.WeaponSlots).Select(id => (KnownObject)id).ToArray();
        var plain = play.Start(level, new PlayerLoadout(0, new Dictionary<KnownObject, int>(), new PlayerImplants(0, 0, 0, 0)), 1);
        plain.PlayUntil(100);
        int[] aims = weapons.Select(id => (int)plain.Map.FindVid(id)!.Aim).ToArray();
        var implanted = play.Start(level, new PlayerLoadout(0, new Dictionary<KnownObject, int>(), new PlayerImplants(0, 0, 0, accuracyImplants)), 1);
        implanted.PlayUntil(100);

        Assert.Contains(aims, aim => aim > 0);
        Assert.Equal(aims.Select(aim => Enumerable.Range(0, accuracyImplants).Aggregate(aim, (scaled, _) => scaled * 6 / 10)), weapons.Select(id => (int)implanted.Map.FindVid(id)!.Aim));
    }

    private static RouteAnalysis EmptyLevel(params KnownObject[] pickups)
    {
        var lattice = new Lattice(20, 14, 200, 400);
        var tour = Enumerable.Range(0, 16).Select(index => new Cell(index, 0)).ToArray();
        var cells = tour.ToHashSet();
        var start = lattice.GetCenter(tour[0]);
        var position = new Vector3(start.X, start.Y, 0);
        var placed = pickups.Select((id, index) => new MergedMapSprite(index + 1, id, position, default, Army.Neutral, [])).ToList();
        var elements = placed.Select(placement => new RouteElement
        {
            Kind = ElementKind.Pickup, Placement = placement, Id = placement.Id, X = (int)start.X, Y = (int)start.Y, Cell = tour[0], Hidden = false, Direction = 0
        }).ToArray();
        placed.Insert(0, new MergedMapSprite(0, KnownObject.PlayerStart, position, default, Army.Player, []));

        return new RouteAnalysis
        {
            Name = "Campaign test", WallSet = WallSet.Basement,
            Model = new LevelModel
            {
                Name = "Campaign test", WallSet = WallSet.Basement, Lattice = lattice, Cells = cells, Edges = new Dictionary<EdgeKey, EdgeKind>(), Objects = [], Sprites = placed, StoredSprites = placed,
                MapSize = new SizeF(8192, 8192), Version = 12
            },
            Step = lattice.CellStep, ReachableCells = cells.Count, RoutePixels = (int)(lattice.CellStep * (tour.Length - 1)), SpinePixels = 0,
            RevisitShare = 0, Legs = [], Objectives = [], Elements = elements, Gating = new GateMeasurement(), Checkpoints = [],
            FallbackUsage = new FallbackUsage(false, false, 0), Tour = tour,
            Grid = new RouteGrid { Lattice = lattice, Cells = cells, Edges = [], FenceEdges = [], BlockedCells = [] }
        };
    }
}

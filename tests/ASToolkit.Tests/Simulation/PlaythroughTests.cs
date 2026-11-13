using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Simulation;
using ASToolkit.Core.Routes;
using System.Numerics;
using Xunit;

namespace ASToolkit.Tests.Simulation;

public class PlaythroughTests
{
    [Fact]
    public void Run_TriggersBlackoutAndBreakerWhenThePlayerEntersTheirAreas()
    {
        string game = TestGameFolder.RequireFolder();
        var lattice = new Lattice(20, 14, 200, 400);
        var tour = Enumerable.Range(0, 16).Select(index => new Cell(index, 0)).ToArray();
        var cells = tour.ToHashSet();
        var blackout = lattice.GetCenter(tour[4]);
        var breaker = lattice.GetCenter(tour[12]);
        var analysis = new RouteAnalysis
        {
            Name = "Blackout",
            WallSet = WallSet.Basement,
            Model = new LevelModel
            {
                Name = "Blackout", WallSet = WallSet.Basement, Lattice = lattice, Cells = cells, Edges = new Dictionary<EdgeKey, EdgeKind>(),
                Objects = [], Sprites = [new MergedMapSprite(1, KnownObject.Breaker, new Vector3(breaker.X, breaker.Y, 0), 0, 0, [])]
            },
            Step = lattice.CellStep,
            ReachableCells = cells.Count,
            RoutePixels = (int)(lattice.CellStep * (tour.Length - 1)),
            SpinePixels = 0,
            RevisitShare = 0,
            Legs = [],
            Objectives =
            [
                new RouteObjective(ObjectiveKind.Blackout, blackout.X, blackout.Y, 0, 40),
                new RouteObjective(ObjectiveKind.Breaker, breaker.X, breaker.Y, 0)
            ],
            Elements = [],
            Gating = new GateMeasurement(),
            Checkpoints = [],
            FallbackUsage = new FallbackUsage(false, false, 0),
            Tour = tour,
            Grid = new RouteGrid { Lattice = lattice, Cells = cells, Edges = [], FenceEdges = [], BlockedCells = [] }
        };
        var table = TestGameFolder.Table;
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);
        var result = new Playthrough(game, TestGameFolder.Objects, table).Run(analysis, loadout, 1);

        Assert.True(result.Survived);
        Assert.False(result.Script.DoorsInitiallyLocked);
        Assert.NotNull(result.Script.BlackoutAt);
        Assert.NotNull(result.Script.DoorsOpenedAt);
        Assert.True(result.Script.BlackoutAt > 0);
        Assert.True(result.Script.DoorsOpenedAt > result.Script.BlackoutAt);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Run_NavigatesOriginalTerrainWithThePlayersFootprint(int number)
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var table = TestGameFolder.Table;
        string name = $"Level_{number:00}.map";
        var level = new LevelLoader(objects).Load(name, Path.Combine(game, "Maps", name))!;
        var loadout = new PlayerLoadout(KnownObject.RedArmor, new Dictionary<KnownObject, int>
        {
            [KnownObject.Shotgun] = 100, [KnownObject.Minigun] = 800, [KnownObject.RocketLauncher] = 80, [KnownObject.PlasmaRifle] = 50
        }, new PlayerImplants(3, 3, 3, 3));
        var result = new Playthrough(game, objects, table).Run(level.Analysis, loadout, 1);
        Assert.True(result.Survived, $"{name}: progress {result.Progress:P1}, time {result.Time} ms");
        if (number == 4)
        {
            Assert.NotNull(result.Script.BlackoutAt);
            Assert.NotNull(result.Script.DoorsOpenedAt);
            Assert.True(result.Script.DoorsOpenedAt > result.Script.BlackoutAt);
        }
    }

    [Fact]
    public void Run_PlaysTheFirstLabLevelToItsEndTheSameWayForASeed()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var table = TestGameFolder.Table;
        var level = new LevelLoader(objects).Load("Level_02.map", Path.Combine(game, "Maps", "Level_02.map"));
        Assert.NotNull(level);
        var playthrough = new Playthrough(game, objects, table);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);

        var first = playthrough.Run(level.Analysis, loadout, 1, true);
        var second = playthrough.Run(level.Analysis, loadout, 1, true);

        Assert.True(first.Survived, $"Progress {first.Progress:P1}, time {first.Time} ms, kills {first.Kills}, minimum health {first.MinHealth}, last player {first.Replay!.Frames.LastOrDefault()?.Units[0]}");
        Assert.Equal(level.Analysis.Elements.Count(element => element.Kind == ElementKind.Monster), first.Kills);
        Assert.Equal(first.Time, second.Time);
        Assert.Equal(first.Replay!.Shots.Count, second.Replay!.Shots.Count);
        Assert.NotEmpty(first.Replay!.Frames);
        Assert.NotEmpty(first.Replay!.Scenery);
        Assert.Contains(first.Replay!.EffectFrames, frame => frame.Effects.Any(effect => effect.Id == KnownObject.PistolMuzzleFlash));
        Assert.Contains(first.Replay!.EffectFrames, frame => frame.Effects.Any(effect => effect.Id == KnownObject.FlyingCasing));
        Assert.Contains(first.Replay!.Scenery, effect => effect.Id == KnownObject.LyingCasing);
        Assert.NotEmpty(first.Replay!.PickupEvents);
        Assert.Equal(first.Replay!.PickupEvents, second.Replay!.PickupEvents);
        Assert.All(first.Replay!.PickupEvents, pickup => Assert.Contains(pickup.Placement, level.Map.Sprites));
        Assert.Contains(first.Replay!.Scenery, effect => effect.Id == KnownObject.SmallBloodPool);
        Assert.Equal(first.Replay!.Scenery, second.Replay!.Scenery);

        var movement = new MovementMap(level.Model, new SpritePartReader(game, objects));
        // Check static obstacles only, and only units that could stand clear of them before
        foreach (var door in level.Model.Sprites.Where(sprite => sprite.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor))
        {
            movement.Remove(door);
        }

        int removal = 0;
        var couldStand = new Dictionary<int, bool>();
        foreach (var frame in first.Replay!.Frames)
        {
            while (removal < first.Replay!.RemovedSprites.Count && first.Replay!.RemovedSprites[removal].Time <= frame.Time)
            {
                movement.Remove(first.Replay!.RemovedSprites[removal++].Placement);
            }

            bool doorsLocked = (first.Script.DoorsInitiallyLocked || frame.Time >= first.Script.BlackoutAt) &&
                (first.Script.DoorsOpenedAt is null || frame.Time < first.Script.DoorsOpenedAt);
            movement.SetGates(doorsLocked, first.Script.FencesDownAt is null || frame.Time < first.Script.FencesDownAt);
            foreach (var unit in frame.Units.Where(unit => unit.DiedAt < 0))
            {
                Assert.True(objects.TryFind(unit.Id, out var obj));
                bool stands = movement.CanStand(obj, unit.Position);
                Assert.True(stands || unit.Animation != SpriteAnimation.Go || !couldStand.GetValueOrDefault(unit.Unit, true),
                    $"{unit.Id} walked into terrain or a static obstacle at {unit.Position}, height {unit.Position.Z}, time {frame.Time}");
                couldStand[unit.Unit] = stands;
            }
        }
    }
}

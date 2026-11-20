using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Simulation;
using ASToolkit.Core.Routes;
using Xunit;

namespace ASToolkit.Tests.Simulation;

public class PlaythroughTests
{
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

        Assert.True(first.Survived, $"Progress {first.Progress:P1}, time {first.Time} ms, kills {first.Kills}, minimum health {first.MinHealth}");
        Assert.Equal(level.Analysis.Elements.Count(element => element.Kind == ElementKind.Monster), first.Kills);
        Assert.Equal(first.Time, second.Time);
        Assert.Equal(first.Demo!.Value.Body.ToArray(), second.Demo!.Value.Body.ToArray());
        Assert.NotEmpty(first.Pickups);
        Assert.Equal(first.Pickups, second.Pickups);

        var moved = new HashSet<MergedMapSprite>(ReferenceEqualityComparer.Instance);
        var replay = playthrough.LoadReplay(level.Analysis, loadout, first.Demo.Value);
        var placed = new Dictionary<MergedMapSprite, Vector3>(ReferenceEqualityComparer.Instance);
        foreach (var placement in replay.Sprites)
        {
            var sprite = replay.FindPlacementSprite(placement);

            if (sprite is not null)
            {
                placed[placement] = sprite.Position;
            }
        }

        var effects = new HashSet<KnownObject>();
        foreach (var map in PlayDemo(replay))
        {
            foreach (var effect in map.Effects.Active)
            {
                effects.Add(effect.Definition.Id);
            }

            foreach (var placement in placed.Keys.ToArray())
            {
                var sprite = map.FindPlacementSprite(placement);

                if (sprite is not null && !map.IsRemoved(placement) && sprite.Position != placed[placement])
                {
                    moved.Add(placement);
                    placed[placement] = sprite.Position;
                }
            }
        }

        Assert.Contains(KnownObject.PistolMuzzleFlash, effects);
        Assert.Contains(KnownObject.FlyingCasing, effects);
        Assert.Contains(replay.Effects.Settled, effect => effect.Id == KnownObject.LyingCasing);
        Assert.Contains(replay.Effects.Settled, effect => effect.Id == KnownObject.SmallBloodPool);

        var parts = new SpritePartReader(game, objects);
        var terrain = new MovementMap(level.Model, parts);
        var movement = new WalkObstacles(level.Model, parts, objects, terrain);

        foreach (var moving in level.Model.Sprites.Where(sprite => sprite.Id is KnownObject.SmallDoor or KnownObject.DoubleDoor).Concat(moved).Distinct())
        {
            movement.Remove(moving);
        }

        var removed = new HashSet<MergedMapSprite>(ReferenceEqualityComparer.Instance);
        var couldStand = new Dictionary<Sprite, bool>();
        int checkedAt = -1;
        foreach (var map in PlayDemo(playthrough.LoadReplay(level.Analysis, loadout, first.Demo.Value)))
        {
            int time = map.Time;

            if (time / 100 == checkedAt / 100)
                continue;

            checkedAt = time;
            foreach (var placement in map.Sprites)
            {
                if (map.IsRemoved(placement) && removed.Add(placement))
                {
                    movement.Remove(placement);
                    terrain.Remove(placement);
                }
            }

            bool doorsLocked = (first.Script.DoorsInitiallyLocked || time >= first.Script.BlackoutAt) &&
                (first.Script.DoorsOpenedAt is null || time < first.Script.DoorsOpenedAt);
            movement.SetGates(doorsLocked, first.Script.FencesDownAt is null || time < first.Script.FencesDownAt);
            foreach (var unit in map.Monsters.Append(map.Player))
            {
                var obj = unit.Definition.Object;

                if (unit.Owner?.Link == unit || obj.SpriteClass is not (SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man) || unit.Animation >= SpriteAnimation.Death)
                    continue;

                bool stands = movement.CanStand(obj, unit.Position);
                Assert.True(stands || unit.Animation != SpriteAnimation.Go || !couldStand.GetValueOrDefault(unit, true),
                    $"{unit.Definition.Id} walked into terrain or a static obstacle at {unit.Position}, height {unit.Position.Z}, time {time}");
                couldStand[unit] = stands;
            }
        }
    }

    private static IEnumerable<Map> PlayDemo(Map map)
    {
        uint now = 0;
        while (true)
        {
            map.StepDemo(now, out now);

            if (!map.IsPlayingDemo)
                yield break;

            map.Step();
            yield return map;

            if (map.TryGetNextDemoTime(out int next))
            {
                map.AdvanceClock(map.WallTime + (uint)(next - map.Time));
            }
        }
    }
}

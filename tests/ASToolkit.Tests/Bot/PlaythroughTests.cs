using System.Numerics;
using ASToolkit.Analysis.Reference;
using ASToolkit.Analysis.Routes;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Tests.Engine;
using Xunit;

namespace ASToolkit.Tests.Bot;

public class PlaythroughTests
{
    public static PlaythroughResult RunOriginal(int number)
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        string name = $"Level_{number:00}.map";
        var level = new LevelLoader(objects).Load(name, Path.Combine(game, "Maps", name))!;
        var loadout = new PlayerLoadout(KnownObject.RedArmor, new Dictionary<KnownObject, int>
        {
            [KnownObject.Shotgun] = 100, [KnownObject.Minigun] = 800, [KnownObject.RocketLauncher] = 80, [KnownObject.PlasmaRifle] = 50
        }, new PlayerImplants(3, 3, 3, 3));
        return new Playthrough(game, objects, TestGameFolder.Table).Run(level.Analysis, loadout, 1);
    }

    [Fact]
    public void Run_LevelThree_NavigatesOriginalTerrainWithThePlayersFootprint()
    {
        var result = RunOriginal(3);
        Assert.True(result.Survived, $"Level_03.map: progress {result.Progress:P1}, time {result.Time} ms");
    }

    [Fact]
    public void Run_PlaysTheFirstLabLevelToItsEndTheSameWayForASeed()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var level = new LevelLoader(objects).Load("Level_02.map", Path.Combine(game, "Maps", "Level_02.map"));
        Assert.NotNull(level);
        var playthrough = new Playthrough(game, objects, TestGameFolder.Table);
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);

        var first = playthrough.Run(level.Analysis, loadout, 1, true);
        var second = playthrough.Run(level.Analysis, loadout, 1, true);

        Assert.True(first.Survived, $"Progress {first.Progress:P1}, time {first.Time} ms, kills {first.Kills}, minimum health {first.MinHealth}");
        Assert.Equal(level.Analysis.Elements.Count(element => element.Kind == ElementKind.Monster), first.Kills);
        Assert.Equal(first.Time, second.Time);
        Assert.Equal(first.Demo!.Value.Body.ToArray(), second.Demo!.Value.Body.ToArray());
        Assert.NotEmpty(first.Pickups);
        Assert.Equal(first.Pickups, second.Pickups);

        var replay = playthrough.LoadReplay(level.Analysis, loadout, first.Demo.Value);
        var field = new WalkField(level.Analysis.Grid, true, replay.Movement, replay.Collector, replay.Definitions.Find(KnownObject.PlayerStart)!.Object);
        var effects = new HashSet<KnownObject>();
        var previous = new Dictionary<Sprite, Vector3>();
        int checkedAt = -1;
        var observer = new MapObserver(replay);
        foreach (var map in PlayDemo(replay))
        {
            observer.Observe();
            foreach (var effect in map.GetEffects())
            {
                effects.Add(effect.Definition.Id);
            }

            int time = map.Time;

            if (time / 100 == checkedAt / 100)
                continue;

            checkedAt = time;
            foreach (var unit in map.Monsters.Append(map.Player))
            {
                ref readonly var obj = ref unit.Definition.Object;

                if (unit.Owner?.Link == unit || obj.SpriteClass is not (SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man) || unit.Animation >= SpriteAnimation.Death)
                    continue;

                bool walkedIn = unit.Animation == SpriteAnimation.Go && previous.TryGetValue(unit, out var from) &&
                    field.CanStand(obj, from) && !field.CanStand(obj, unit.Position) && !TouchesPushedSprite(map, unit);
                Assert.False(walkedIn, $"{unit.Definition.Id} walked into terrain or a static obstacle at {unit.Position}, height {unit.Position.Z}, time {time}");
                previous[unit] = unit.Position;
            }
        }

        Assert.Contains(KnownObject.PistolMuzzleFlash, effects);
        Assert.Contains(KnownObject.FlyingCasing, effects);
        Assert.Contains(observer.BakedEffects, effect => effect.Id == KnownObject.LyingCasing);
        Assert.Contains(observer.BakedEffects, effect => effect.Id == KnownObject.SmallBloodPool);
    }

    private static bool TouchesPushedSprite(Map map, Sprite unit)
    {
        var point = unit.Position.AsVector2();
        var half = unit.Definition.Half;
        var reach = half + map.Collector.LargestHalf;
        foreach (var sprite in map.Collector.GetNear(point - reach, point + reach))
        {
            var definition = sprite.Definition;
            var offset = Vector2.Abs(sprite.Position.AsVector2() - point);

            if (sprite != unit && definition.CanBePushed && (definition.Id == KnownObject.Crate || definition.Object.SpriteClass is not (SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man)) &&
                offset.X < definition.Half.X + half.X && offset.Y < definition.Half.Y + half.Y)
                return true;
        }

        return false;
    }

    private static IEnumerable<Map> PlayDemo(Map map)
    {
        uint now = 0;
        while (true)
        {
            uint wallTime = map.TryGetNextDemoTime(out int next) ? map.WallTime + (uint)(next - map.Time) : map.GetPacer().TakeNextWallTime(map);
            if (map.StepAimed(wallTime, now, out now) || !map.IsPlayingDemo)
                yield break;

            yield return map;
        }
    }
}

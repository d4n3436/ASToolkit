using System.Numerics;
using ASToolkit.Analysis.Reference;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ReferenceCountTests
{
    private const int PlayerSlots = 4;

    [Fact]
    public void ReferenceCount_EveryFrameOfABotRun_MatchesTheSpritesHolders()
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var level = new LevelLoader(objects).Load("Level_02.map", Path.Combine(game, "Maps", "Level_02.map"))!;
        var loadout = new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);
        var session = new Playthrough(game, objects, TestGameFolder.Table).Start(level.Analysis, loadout, 1);
        var map = session.Map;

        AssertCounts(map);
        while (session is { IsPlaying: true, HasWon: false } && map.Time < PlaySession.MaxTime)
        {
            int time = map.Time;
            session.PlayUntil(time + 1);
            AssertCounts(map);

            if (map.Time == time)
                break;
        }
    }

    private static void AssertCounts(Map map)
    {
        var expected = new Dictionary<Sprite, int>(ReferenceEqualityComparer.Instance);
        for (int layer = 0; layer < Map.LayerCount; layer++)
        {
            foreach (var entry in map.GetLayer(layer))
            {
                Hold(expected, entry);
            }
        }

        foreach (var sprite in map.Collector.GetNear(Vector2.Zero, map.Size.ToVector2()))
        {
            Hold(expected, sprite);
        }

        foreach (var sprite in map.Collector.Listed)
        {
            Hold(expected, sprite);
        }

        foreach (var group in map.Groups)
        {
            foreach (var member in group.Members)
            {
                Hold(expected, member);
            }
        }

        foreach (var sprite in map.MenuSprites)
        {
            Hold(expected, sprite);
        }

        for (int army = 0; army < PlayerSlots; army++)
        {
            Hold(expected, map.Flagman(army));
        }

        Hold(expected, map.CursorSprite);

        foreach (var holder in expected.Keys.ToArray())
        {
            Hold(expected, holder.Goal);
        }

        foreach ((var sprite, int count) in expected)
        {
            Assert.True(sprite.ReferenceCount == count, $"time {map.Time}: {sprite.Vid.Id} #{sprite.Number} holds {sprite.ReferenceCount} references, expected {count}");
        }
    }

    private static void Hold(Dictionary<Sprite, int> expected, Sprite? sprite)
    {
        if (sprite is not null)
        {
            expected[sprite] = expected.GetValueOrDefault(sprite) + 1;
        }
    }
}

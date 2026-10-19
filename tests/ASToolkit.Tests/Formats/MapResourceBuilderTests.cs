using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class MapResourceBuilderTests
{
    [Fact]
    public void Load_OriginalLevels_MatchTheFixtureReader()
    {
        string game = TestGameFolder.RequireFolder();
        foreach ((string level, var expected) in FixtureReader.Read<Dictionary<string, MapRow>>("maps.json"))
        {
            var map = MapResourceBuilder.Load(Path.Combine(game, "Maps", level));
            Assert.Equal(expected.Count, map.Sprites.Count);
            Assert.Equal(expected.Ids, map.Sprites.Sum(s => (long)s.Id));
            Assert.Equal(expected.X, map.Sprites.Sum(s => s.X), 0.1);
            Assert.Equal(expected.Y, map.Sprites.Sum(s => s.Y), 0.1);
            Assert.Equal(expected.PayloadBytes, map.Sprites.Sum(s => s.Payload.Length));
            Assert.Equal(expected.Army, map.Sprites.Sum(s => s.Army));
        }
    }

    [Fact]
    public void Write_LoadedLevel_ReadsBackTheSameSprites()
    {
        string game = TestGameFolder.RequireFolder();
        string source = Path.Combine(game, "Maps", "Level_04.map");
        var map = MapResourceBuilder.Load(source);
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Level_04.map");
        map.Write(path, source);
        var back = MapResourceBuilder.Load(path);
        Assert.Equal(map.Sprites.Count, back.Sprites.Count);
        for (int spriteIndex = 0; spriteIndex < map.Sprites.Count; spriteIndex++)
        {
            Assert.Equal(map.Sprites[spriteIndex].Id, back.Sprites[spriteIndex].Id);
            Assert.Equal(map.Sprites[spriteIndex].X, back.Sprites[spriteIndex].X);
            Assert.Equal(map.Sprites[spriteIndex].Payload, back.Sprites[spriteIndex].Payload);
        }
    }

    private sealed record MapRow(int Count, long Ids, double X, double Y, int PayloadBytes, long Army);
}

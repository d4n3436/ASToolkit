using ASToolkit.Core.Engine;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MapNameTests
{
    [Fact]
    public void Load_SecondLevel_KeepsThePreviousName()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var session = new GameSession(new ScriptRegistry(""));

        var map = EngineTestMap.CreateMap(definitions, session: session);
        map.Load(EngineTestMap.CreateLevel(definitions), 1);

        Assert.Equal("maps\\engine_test.map"u8.ToArray(), session.MapName.ToArray());
        Assert.Equal("maps\\engine_test.map"u8.ToArray(), session.PreviousMapName.ToArray());
    }

    [Fact]
    public void MapName_WithTheScriptsText_ReadsItBack()
    {
        var level = new LevelTemplate
        {
            MapData = new MapData { Sprites = [], StoredSprites = [] },
            Definitions = null!,
            Movement = new MovementMap(),
            AmmoBatches = [],
            FrameTimes = [],
            FileSystem = null!,
            ScriptPath = "maps\\a.lgc"
        };

        Assert.Equal("maps\\a.map", level.MapName);
        Assert.Equal("maps\\b.map", (level with { MapName = "maps\\b.map" }).MapName);
    }
}

using ASToolkit.Bot;
using ASToolkit.Engine;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MapNameTests
{
    [Fact]
    public void Load_SecondLevel_KeepsThePreviousName()
    {
        var vids = TestGameFolder.CreateCatalog();
        var session = new GameSession(new Registry(""), time: new LoadingRedrawClock());

        var map = EngineTestMap.CreateMap(vids, session: session);
        map.Load(EngineTestMap.MapName, GameClock.StartWallTime);

        Assert.Equal("maps\\engine_test.map"u8.ToArray(), session.MapName.ToArray());
        Assert.Equal("maps\\engine_test.map"u8.ToArray(), session.PreviousMapName.ToArray());
    }
}

using System.Text;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class MouseTipsTests
{
    [Fact]
    public void GetMouseTipsString_PlayerHoversASprite_ReadsTheUnitsNameOfItsObjectId()
    {
        var map = EngineTestMap.CreateMap();
        ((MemoryWriteOverlay)map.FileSystem).Set(Map.StringsFile, Encoding.ASCII.GetBytes($"[Units]\r\n{(int)map.Flagman(0)!.Vid.Id}=Hero\r\n"));
        map.Player(Map.ControlledPlayer).Hovered = map.Flagman(0);

        var text = map.GetMouseTipsString();
        map.PauseOn();
        var paused = map.GetMouseTipsString();

        Assert.Equal("Hero", Encoding.ASCII.GetString(text.Span));
        Assert.True(paused.IsEmpty);
    }

    [Fact]
    public void GetMouseTipsString_NothingHovered_IsEmpty()
    {
        var map = EngineTestMap.CreateMap();
        ((MemoryWriteOverlay)map.FileSystem).Set(Map.StringsFile, Encoding.ASCII.GetBytes($"[Units]\r\n{(int)map.Flagman(0)!.Vid.Id}=Hero\r\n"));

        Assert.True(map.GetMouseTipsString().IsEmpty);
    }
}

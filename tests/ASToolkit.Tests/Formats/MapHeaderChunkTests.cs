using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class MapHeaderChunkTests
{
    [Fact]
    public void Header_Version9_ConvertsIntegerFields()
    {
        var map = new MapResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "survive_01.map")));

        Assert.Equal(9, map.Header.Version);
        Assert.Equal(1800, map.Header.MapW);
        Assert.Equal(1800, map.Header.MapH);
        Assert.Equal(455, map.Header.Camera.X);
    }

    [Fact]
    public void HeightGrid_SurvivalMap_HoldsOneCellPerEightPixels()
    {
        var map = new MapResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "survive_01.map")));

        Assert.Equal(225 * 225, map.HeightGrid.Length);
        Assert.Equal(4, map.PlayerHandles.Length);
    }

    [Fact]
    public void HeightGrid_Level_IsEmptyWithoutAGridChunk()
    {
        var map = new MapResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "Level_02.map")));

        Assert.True(map.HeightGrid.IsEmpty);
        Assert.All(map.PlayerHandles.ToArray(), handle => Assert.Equal(0, handle));
    }
}

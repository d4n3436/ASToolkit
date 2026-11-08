using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class GameTableTests
{
    [Fact]
    public void Load_ReadsTheLevelMoneyAndWeaponPrices()
    {
        string game = TestGameFolder.RequireFolder();
        var table = GameTable.Load(Path.Combine(game, "Maps", "common_table.lgc"));

        Assert.Equal([100, 100, 350, 1000, 2800, 5500, 9000, 18000, 29000, 32000, 0, 0], table.GetArray("LevelMoney"));
        Assert.Equal([0, 0, 200, 350, 1000, 2500, 4800, 8000, 15800, 25400, 900], table.GetArray("WeaponPrice"));
    }

    [Fact]
    public void Parse_SkipsCommentsAndReadsSizedAndSignedArrays()
    {
        var table = GameTable.Parse("""
            // static int Hidden[] = { 1 };
            static int Sized[3] = { 1, -2 ,3 }; /* static int Block[] = { 4 }; */
            static int Split[] =
            {
                5, // five
                6,
            };
            static int Single;
            """);

        Assert.Equal(["Sized", "Split"], table.Names.Order());
        Assert.Equal([1, -2, 3], table.GetArray("Sized"));
        Assert.Equal([5, 6], table.GetArray("Split"));
        Assert.False(table.TryGetArray("Hidden", out var hidden));
        Assert.Null(hidden);
        Assert.Throws<KeyNotFoundException>(() => table.GetArray("Block"));
    }
}

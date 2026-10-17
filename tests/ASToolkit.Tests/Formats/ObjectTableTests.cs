using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class ObjectTableTests
{
    [Fact]
    public void Load_MatchesTheFixtureTable()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var expected = FixtureReader.Read<List<ObjectRow>>("objects.json");
        Assert.Equal(expected.Count, table.All.Count);
        foreach (var row in expected)
        {
            var o = table.Find((KnownObject)row.Id);
            Assert.NotNull(o);
            Assert.Equal(row.Flags, o.Flags);
            Assert.Equal(row.Type, o.Type);
            Assert.Equal(row.Hp, o.HitPoints);
            Assert.Equal(row.Path, o.Path);
            Assert.Equal(row.Ext[0], o.ExtentX, 3);
            Assert.Equal(row.Ext[1], o.ExtentY, 3);
            Assert.Equal(row.Ext[2], o.ExtentZ, 3);
        }
    }

    private sealed record ObjectRow(int Id, uint Flags, uint Type, IReadOnlyList<double> Ext, int Hp, string Path);
}

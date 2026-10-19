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
        Assert.Equal(expected.Count, table.Ids.Count);
        foreach (var row in expected)
        {
            var id = (KnownObject)row.Id;
            Assert.True(table.TryFind(id, out var obj));
            Assert.Equal(row.Flags, (uint)obj.Flags);
            Assert.Equal(row.Type, obj.Type);
            Assert.Equal(row.Hp, obj.HitPoints);
            Assert.Equal(row.Path, table.GetPath(id));
            Assert.Equal(row.Ext[0], obj.ExtentX, 3);
            Assert.Equal(row.Ext[1], obj.ExtentY, 3);
            Assert.Equal(row.Ext[2], obj.ExtentZ, 3);
        }
    }

    private sealed record ObjectRow(int Id, uint Flags, uint Type, IReadOnlyList<double> Ext, int Hp, string Path);
}

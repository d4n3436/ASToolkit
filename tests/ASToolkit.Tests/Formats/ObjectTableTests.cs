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
            Assert.Equal(row.Type, (uint)obj.SpriteClass);
            Assert.Equal(row.Hp, obj.MaxHp);
            Assert.Equal(row.Path, table.GetPath(id));
            Assert.Equal(row.Ext[0], obj.Size.X, 3);
            Assert.Equal(row.Ext[1], obj.Size.Y, 3);
            Assert.Equal(row.Ext[2], obj.Size.Z, 3);
        }
    }

    [Fact]
    public void Load_ReadsTheSpriteFieldsOfEveryEntry()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        foreach (var id in table.Ids)
        {
            Assert.True(table.TryFind(id, out var obj));
            Assert.InRange(obj.Directions, 1, 256);
            Assert.InRange(obj.FramesPerDirection, 0, 4096);
            Assert.InRange(obj.GammaRed, -255, 255);
            Assert.InRange(obj.GammaGreen, -255, 255);
            Assert.InRange(obj.GammaBlue, -255, 255);
            Assert.InRange(obj.GammaAlpha, -255, 255);
        }
    }

    [Fact]
    public void Load_ReadsTheTorsoLinkedToThePlayerLegs()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        Assert.True(table.TryFind(table.IsAlienShooter2 ? (KnownObject)250 : KnownObject.PlayerStart, out var legs));
        Assert.True(table.TryFind(legs.Link, out var torso));
        Assert.Equal(SpriteType.Sprite, torso.SpriteType);
        Assert.Equal(table.IsAlienShooter2 ? 0 : 33, legs.LinkOffset.Z);
    }

    private sealed record ObjectRow(int Id, uint Flags, uint Type, IReadOnlyList<double> Ext, int Hp, string Path);
}

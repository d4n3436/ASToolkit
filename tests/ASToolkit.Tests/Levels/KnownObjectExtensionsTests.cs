using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;
using Xunit;

namespace ASToolkit.Tests.Levels;

public class KnownObjectExtensionsTests
{
    [Fact]
    public void Classes_Pickups_TakeTheirCategories()
    {
        Assert.Equal(ItemCategory.Money, KnownObject.Money.PickupCategory);
        Assert.Equal(ItemCategory.Ammo, KnownObject.ShotgunAmmo.PickupCategory);
        Assert.True(KnownObject.Shotgun.IsWeapon);
        Assert.True(KnownObject.ShotgunAmmo.IsAmmo);
        Assert.Null(KnownObject.Table.PickupCategory);
    }

    [Fact]
    public void Classes_Decor_TakeTheirFamilies()
    {
        Assert.Equal(PieceFamily.Table, KnownObject.Table.Family);
        Assert.Equal(PieceFamily.Chair, KnownObject.Chair.Family);
        Assert.True(KnownObject.Chair.IsChair);
        Assert.True(KnownObject.Monitor.IsWallMounted);
        Assert.Null(KnownObject.EmbeddedShadow.Family);
        Assert.True(KnownObject.SmallBloodPool.IsBlood);
    }

    [Fact]
    public void Cluster_ChainOfPoints_JoinsWithinReach()
    {
        PointF[] points = [new(0, 0), new(30, 0), new(60, 0), new(200, 0)];

        var groups = points.Cluster(45);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.Count == 3);
        Assert.Contains(groups, group => group.Count == 1);
    }

    [Fact]
    public void Draw_WholeRate_DrawsThatManyDistinctOfTheNearestFacing()
    {
        var companion = new ObjectCompanion
        {
            Id = KnownObject.ZDummy,
            Rate = 2,
            Samples = [new AnchorSample(0, 1, 2, 0, 0), new AnchorSample(128, 5, 6, 0, 128), new AnchorSample(128, 7, 8, 0, 128)]
        };

        var drawn = companion.Draw(new Random(1), 120);

        Assert.Equal(2, drawn.Length);
        Assert.All(drawn, sample => Assert.Equal(128, sample.AnchorFacing));
        Assert.NotEqual(drawn[0], drawn[1]);
    }

    [Fact]
    public void Draw_RateAboveTheSamples_DrawsEachSampleOnce()
    {
        var companion = new ObjectCompanion { Id = KnownObject.ZDummy, Rate = 3, Samples = [new AnchorSample(0, 1, 2, 0, 0)] };

        Assert.Single(companion.Draw(new Random(1), 0));
    }
}

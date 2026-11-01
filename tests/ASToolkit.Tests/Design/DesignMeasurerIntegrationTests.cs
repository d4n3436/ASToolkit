using ASToolkit.Core.Design;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Reference;
using Xunit;

namespace ASToolkit.Tests.Design;

[Trait("Category", "Integration")]
public class DesignMeasurerIntegrationTests
{
    private static readonly Lazy<DesignMeasures[]> Campaign = new(() =>
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        return ReferenceLevelSet.CampaignLevels.Select(level =>
        {
            string path = Path.Combine(game, "Maps", ReferenceLevelSet.GetCampaignName(level));
            var measures = DesignMeasurer.Measure(Path.GetFileName(path), MapResourceBuilder.Load(path), LevelScriptReader.Read(Path.ChangeExtension(path, ".lgc")), objects);
            Assert.NotNull(measures);
            return measures;
        }).ToArray();
    });

    [Fact]
    public void Measure_Campaign_FindsChairsAtTablesAndCorpsesOnBlood()
    {
        Assert.Equal(new MeasureShare(154, 178), Sum(measure => measure.ChairsAtTables));
        Assert.Equal(new MeasureShare(155, 164), Sum(measure => measure.CorpsesOnBlood));
        Assert.Equal(new MeasureShare(52, 52), Sum(measure => measure.BigComputersWithChairs));
    }

    [Fact]
    public void Measure_Campaign_KeepsOfficeDecorOutOfPassages()
    {
        Assert.Equal(new MeasureShare(0, 322), Sum(measure => measure.OfficeDecorInPassages));
    }

    [Fact]
    public void Measure_Campaign_PutsCabinetsOnFarWalls()
    {
        var cabinets = Campaign.Value.SelectMany(measure => measure.Walls).Where(sample => sample.Id == KnownObject.Cabinet).ToArray();

        Assert.Equal(207, cabinets.Length);
        Assert.Equal(173, cabinets.Count(sample => sample.FarOnly));
        Assert.Equal(15, cabinets.Count(sample => sample.NearOnly));
    }

    [Fact]
    public void Measure_Campaign_PutsPickupsAgainstWallsAndInPiles()
    {
        Assert.Equal(new MeasureShare(84, 128), Sum(measure => measure.HealthAgainstWalls));
        Assert.Equal(new MeasureShare(43, 100), Sum(measure => measure.MoneyInCorners));
        Assert.Equal(new MeasureShare(120, 217), Sum(measure => measure.PickupPiles));
    }

    [Fact]
    public void Measure_Campaign_FindsTripletsMirroredPadsAndSitesPassedEarly()
    {
        Assert.Equal(new MeasureShare(9, 9), Sum(measure => measure.MissilesInTriplets));
        Assert.Equal(new MeasureShare(5, 5), Sum(measure => measure.MirroredPads));
        Assert.Equal(new MeasureShare(10, 20), Sum(measure => measure.SitesPassedEarly));
        Assert.All(Campaign.Value, measure => Assert.Equal(0, measure.RepeatedRooms));
    }

    private static MeasureShare Sum(Func<DesignMeasures, MeasureShare> get) => Campaign.Value.Aggregate(default(MeasureShare), (sum, measure) => sum + get(measure));
}

using ASToolkit.Core.Formats;
using ASToolkit.Core.Reference;
using ASToolkit.Core.Survey;
using Xunit;

namespace ASToolkit.Tests.Survey;

[Trait("Category", "Integration")]
public class SectionMeasurerIntegrationTests
{
    private static readonly Lazy<LevelLoader> Loader = new(() => new LevelLoader(ObjectTable.Load(Path.Combine(TestGameFolder.RequireFolder(), "objects.res"))));

    public static TheoryData<string> OriginalLevels()
    {
        var levels = new TheoryData<string>();
        for (int number = 2; number <= 10; number++)
        {
            levels.Add($"Level_{number:00}.map");
        }

        for (int number = 2; number <= 5; number++)
        {
            levels.Add($"ADDON/Level_{number:00}.map");
            levels.Add($"ADDON2/Level_{number:00}.map");
        }

        return levels;
    }

    [Theory]
    [InlineData("Level_02.map", 5, 0.551f)]
    [InlineData("Level_03.map", 15, 0.456f)]
    [InlineData("Level_04.map", 5, 0.666f)]
    [InlineData("Level_05.map", 9, 0.285f)]
    public void Measure_CampaignLab_SplitsTheTourIntoTheMeasuredSections(string level, int count, float largestShare)
    {
        var sections = new SectionMeasurer(Load(level)).Measure();

        Assert.Equal(count, sections.Length);
        Assert.Equal(largestShare, sections.Max(section => section.FloorShare), 0.005f);
    }

    [Theory]
    [InlineData("Level_02.map", SectionArchetype.Wing)]
    [InlineData("Level_03.map", SectionArchetype.Complex)]
    [InlineData("Level_04.map", SectionArchetype.Wing)]
    [InlineData("Level_05.map", SectionArchetype.Hall)]
    [InlineData("Level_07.map", SectionArchetype.Sprawl)]
    [InlineData("Level_09.map", SectionArchetype.Sprawl)]
    [InlineData("ADDON2/Level_03.map", SectionArchetype.Complex)]
    public void Measure_Original_GivesTheLargestSectionItsForm(string level, SectionArchetype archetype)
    {
        var sections = new SectionMeasurer(Load(level)).Measure();

        Assert.Equal(archetype, sections.MaxBy(section => section.FloorShare)!.Archetype);
    }

    [Theory]
    [MemberData(nameof(OriginalLevels))]
    public void Measure_Original_GivesEveryTouredPlaceOneSectionAlongTheTour(string level)
    {
        var survey = Load(level);
        var sections = new SectionMeasurer(survey).Measure();
        var toured = survey.Route.Select(visit => visit.Place).Where(place => place >= 1 && place <= survey.Places.Count).ToHashSet();

        var sectioned = sections.SelectMany(section => section.Places).ToArray();
        Assert.Equal(sectioned.Length, sectioned.Distinct().Count());
        Assert.Subset(sectioned.ToHashSet(), toured);
        Assert.All(sections, section => Assert.Equal(section.Places[0], section.Entrance));
        Assert.Equal(1f, sections.Sum(section => section.FloorShare), 0.001f);
        Assert.Null(sections[0].Boundary);
        for (int index = 1; index < sections.Length; index++)
        {
            var boundary = sections[index].Boundary;
            Assert.NotNull(boundary);
            Assert.Equal(sections[index].Entrance, boundary.Value.ToPlace);
            Assert.Equal(sections[index - 1].Exit, boundary.Value.FromPlace);
            Assert.True(sections[index].TourStart >= sections[index - 1].TourEnd);
        }

        Assert.All(sections, section => Assert.InRange(section.Compactness, 0.01f, 1f));
    }

    private static LevelSurvey Load(string level)
    {
        var loaded = Loader.Value.Load(level, Path.Combine(TestGameFolder.RequireFolder(), "Maps", level));
        Assert.NotNull(loaded);
        return loaded.Survey;
    }
}

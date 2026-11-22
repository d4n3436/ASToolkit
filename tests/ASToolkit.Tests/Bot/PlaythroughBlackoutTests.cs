using Xunit;

namespace ASToolkit.Tests.Bot;

public class PlaythroughBlackoutTests
{
    [Fact]
    public void Run_LevelFour_SurvivesTheBlackoutAndOpensTheDoorsAfterIt()
    {
        var result = PlaythroughTests.RunOriginal(4);
        Assert.True(result.Survived, $"Level_04.map: progress {result.Progress:P1}, time {result.Time} ms");
        Assert.NotNull(result.Script.BlackoutAt);
        Assert.NotNull(result.Script.DoorsOpenedAt);
        Assert.True(result.Script.DoorsOpenedAt > result.Script.BlackoutAt);
    }
}

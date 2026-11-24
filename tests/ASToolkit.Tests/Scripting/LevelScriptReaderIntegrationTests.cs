using System.Drawing;
using ASToolkit.Formats;
using ASToolkit.Analysis.Scripting;
using Xunit;

namespace ASToolkit.Tests.Scripting;

public class LevelScriptReaderIntegrationTests
{
    [Fact]
    public void Read_CheckpointNamingAnotherPoint_RespawnsThere()
    {
        var script = Read("ADDON2", "Level_04.lgc");

        var checkpoint = Assert.Single(script.Triggers, trigger => trigger.Area.Center == new Point(2516, 2420));
        Assert.Equal(ScriptRespawn.Fixed, checkpoint.Respawn);
        Assert.Equal(new Point(1684, 1640), checkpoint.RespawnPoint);

        var blackout = Assert.Single(script.Triggers, trigger => trigger.LightOff is not null);
        Assert.Equal(ScriptRespawn.None, blackout.Respawn);
        Assert.False(blackout.ShouldSwapKeypads);
        Assert.Equal(3000, blackout.Quake);
    }

    [Fact]
    public void Read_BossLevel_WinsOnTheBossDeathAndKillsItsDivers()
    {
        var script = Read("ADDON2", "Level_05.lgc");

        Assert.Equal(2, script.Handlers.Count);
        Assert.All(script.Handlers, handler =>
        {
            Assert.Equal(KnownObject.DiverBoss, handler.Id);
            Assert.True(handler.ShouldWinLevel);
            Assert.Equal([KnownObject.Spawner], handler.Destroys);
            Assert.Equal([KnownObject.Diver], handler.Kills);
        });

        var blackout = Assert.Single(script.Triggers, trigger => trigger.LightOff is not null);
        Assert.Equal(0x1d313du, blackout.LightOff);
        Assert.Equal([KnownObject.Diver], blackout.Kills);
        Assert.Equal(ScriptRespawn.Player, blackout.Respawn);

        var crowd = Assert.Single(script.CrowdRules);
        Assert.Equal(KnownObject.Diver, crowd.Counted);
        Assert.Equal(18, crowd.Max);
        Assert.Equal(4, crowd.Points.Count);
    }

    [Fact]
    public void Read_AddOnLevelsWinningAfterTheSites_SetWinsAfterSites()
    {
        Assert.True(Read("ADDON", "Level_02.lgc").IsWonAfterSites);
        Assert.True(Read("ADDON", "Level_04.lgc").IsWonAfterSites);
        Assert.False(Read("ADDON", "Level_05.lgc").IsWonAfterSites);
        Assert.False(Read("", "Level_02.lgc").IsWonAfterSites);
    }

    private static LevelScriptSummary Read(string folder, string file) => LevelScriptReader.Read(Path.Combine(TestGameFolder.RequireFolder(), "Maps", folder, file), TestGameFolder.Objects.ScriptVersion);
}

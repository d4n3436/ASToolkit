using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using Xunit;

namespace ASToolkit.Tests.Scripting;

public class LevelLogicReaderTests
{
    [Fact]
    public void Read_OriginalBlackoutLevel_FindsTheBlackoutAndTheSpawnerTriggers()
    {
        (string game, var objects) = LoadGame(false);
        string path = Path.Combine(game, "Maps", "Level_04.map");
        var logic = LevelLogicReader.Read(path, MapResourceBuilder.Load(path), objects, QuestBook.Load(game));

        var blackout = Assert.Single(logic.Markers, marker => marker.Kind == ScriptMarkerKind.Blackout);
        Assert.Equal(new Vector3(2004, 2573, 0), blackout.Position);
        Assert.Equal(80, blackout.Radius);
        Assert.Contains(logic.Markers, marker => marker.Kind == ScriptMarkerKind.Spawn && marker.Radius > 0 && marker.Source != marker.Position);
        Assert.Empty(logic.Progression);
    }

    [Fact]
    public void Read_SequelFirstLevel_FollowsTheQuests()
    {
        (string game, var objects) = LoadGame(true);
        string path = Path.Combine(game, "Maps", "Level_01.map");
        var logic = LevelLogicReader.Read(path, MapResourceBuilder.Load(path), objects, QuestBook.Load(game));

        Assert.Contains(logic.Markers, marker => marker.Kind == ScriptMarkerKind.Quest && marker.Label.Contains("1_34: FIND THE ELEVATOR"));
        Assert.Contains(logic.Markers, marker => marker.Kind == ScriptMarkerKind.Exit);
        Assert.Contains(logic.Markers, marker => marker.Kind == ScriptMarkerKind.Opening && marker.Label.Contains("named h3"));
        Assert.Equal(["start quest 1_3:", "destroy gate for"], logic.Progression.Take(2).Select(step => step.Label[..16]));
    }

    [Fact]
    public void Read_SequelLevelScripts_PlaceTheWaterDrainAndTheCountdown()
    {
        (string game, var objects) = LoadGame(true);
        var quests = QuestBook.Load(game);
        string drainPath = Path.Combine(game, "Maps", "Level_04.map");
        var drain = LevelLogicReader.Read(drainPath, MapResourceBuilder.Load(drainPath), objects, quests);
        string countdownPath = Path.Combine(game, "Maps", "Level_06.map");
        var countdown = LevelLogicReader.Read(countdownPath, MapResourceBuilder.Load(countdownPath), objects, quests);

        Assert.Contains(drain.Markers, marker => marker.Kind == ScriptMarkerKind.Opening && marker.Label.StartsWith("level script, once Action10 is 2: remove every"));
        Assert.Contains(countdown.Markers, marker => marker.Kind == ScriptMarkerKind.Failure && marker.Label.StartsWith("level script"));
    }

    [Fact]
    public void Build_SequelFirstLevel_WalksEveryQuestStep()
    {
        (string game, var objects) = LoadGame(true);
        string path = Path.Combine(game, "Maps", "Level_01.map");
        var map = MapResourceBuilder.Load(path);
        var logic = LevelLogicReader.Read(path, map, objects, QuestBook.Load(game));
        var walk = WalkedRouteBuilder.Build(game, map, objects, logic);

        Assert.NotNull(walk);
        Assert.Equal(0, walk.UnreachedSteps);
        Assert.Contains(walk.Segments, segment => segment.Kind == RouteSegmentKind.Teleport);
    }

    private static (string Game, ObjectTable Objects) LoadGame(bool sequel)
    {
        string game = TestGameFolder.RequireFolder();
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        if (objects.IsAlienShooter2 != sequel)
        {
            Assert.Skip(sequel ? "The test game is not Alien Shooter 2." : "The test game is not Alien Shooter.");
        }

        return (game, objects);
    }
}

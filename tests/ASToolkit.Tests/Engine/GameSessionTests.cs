using System.Text;
using ASToolkit.Bot;
using ASToolkit.Formats;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ASToolkit.Tests.Engine;

public sealed class GameSessionTests
{
    private static readonly string[] Maps = ["logo", "level_01", "shop"];

    [Fact]
    public void Load_TwoSessionsAtOnce_LogTheGamesVidMemory()
    {
        TestGameFolder.RequireFolder();
        string[][] logs = new string[2][];
        Parallel.For(0, logs.Length, run => logs[run] = LoadMaps());

        string[] expected =
        [
            "Vid    release 8134656 97710889",
            "Vid    release 8134656 97710889",
            "Vid    release 8134656 97710889",
            "Vid    release 16523264 97710889",
            "Vid    release 8134656 97710889",
        ];
        Assert.Equal(expected, logs[0]);
        Assert.Equal(expected, logs[1]);
    }

    private static string[] LoadMaps()
    {
        var map = EngineTestMap.CreateMap(time: new SteppedClock(new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero))), writeLog: true);
        var fileSystem = map.Session.FileSystem;
        using (map.Session)
        {
            foreach (string name in Maps)
            {
                map.LoadAt(Encoding.ASCII.GetBytes($"maps\\{name}.map"), EngineTestMap.StartRealTime);
            }
        }

        Assert.True(fileSystem.TryRead("logs\\error.log", out var log));
        return GameTextEncoding.Cyrillic.GetString(log.Span).Split("\r\n").Where(line => line.StartsWith("Vid    release ", StringComparison.Ordinal)).ToArray();
    }
}

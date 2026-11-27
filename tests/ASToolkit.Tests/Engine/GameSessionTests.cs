using System.Text;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
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

    [Fact]
    public void Constructor_VSyncInTheConfiguration_LogsItInTheCaps()
    {
        var fileSystem = new MemoryWriteOverlay(new GameFolder(TestGameFolder.RequireFolder()));
        using (new GameSession(Playthrough.DisplaySettings, Playthrough.AdapterModes, new Registry(""), fileSystem, new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero))))
        {
        }

        Assert.True(fileSystem.TryRead("logs\\error.log", out var log));
        string caps = Assert.Single(GameTextEncoding.Cyrillic.GetString(log.Span).Split("\r\n"), line => line.StartsWith("caps=", StringComparison.Ordinal));
        Assert.Equal("caps=ALPHAPALETTE HARDWARE DOUBLEBUFFER DOTPRODUCT3 CAN'T_Z_BLT COLOR32 VSYNC PIXELSHADER=-65276", caps);
    }

    private static string[] LoadMaps()
    {
        var fileSystem = new MemoryWriteOverlay();
        using (var session = new GameSession(Playthrough.DisplaySettings, Playthrough.AdapterModes, EngineTestMap.CreateRegistry(), fileSystem, new SteppedClock(new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)))))
        {
            var map = EngineTestMap.CreateMap(session: session);
            foreach (string name in Maps)
            {
                map.LoadAt(Encoding.ASCII.GetBytes($"maps\\{name}.map"), EngineTestMap.StartRealTime);
            }
        }

        Assert.True(fileSystem.TryRead("logs\\error.log", out var log));
        return GameTextEncoding.Cyrillic.GetString(log.Span).Split("\r\n").Where(line => line.StartsWith("Vid    release ", StringComparison.Ordinal)).ToArray();
    }
}

using System.Text;
using ASToolkit.Core.Generation;
using Xunit;

namespace ASToolkit.Tests.Generation;

public class RandomAddOnInstallerTests : IDisposable
{
    private static readonly string[] Files =
    [
        "Maps/COMMON.LGC", "Maps/SHOP.LGC", "Maps/MAINMENU.LGC", "Maps/common_fame.lgc", "Maps/common_table.lgc", "Maps/PlayWithAddon1.men",
        "Maps/PlayWithoutAddon1.men", "strings.ini"
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "alien-shooter-random-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void InstallAndRemove_GameFiles_AddTheAddOnAndPutEverythingBack()
    {
        string game = CopyGame();
        var before = Files.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(game, f)));

        RandomAddOnInstaller.Install(game, addOnStyle: true, seed: 1, []);

        string common = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(game, "Maps/COMMON.LGC")));
        Assert.Contains("GAME_TYPE_RANDOM", common);
        Assert.Contains("#define RANDOM_LEVELS 5", File.ReadAllText(Path.Combine(game, "Maps/common_table.lgc"), Encoding.Latin1));
        Assert.Contains("Addon3=GENERATED", File.ReadAllText(Path.Combine(game, "strings.ini"), Encoding.Latin1));
        Assert.Contains("Addon3", Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(game, "Maps/PlayWithAddon1.men"))));
        Assert.True(File.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName, "Level_05.map")));
        Assert.False(File.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName, "Level_06.map")));

        RandomAddOnInstaller.Install(game, addOnStyle: false, seed: 1, []);
        Assert.Contains("#define RANDOM_LEVELS 10", File.ReadAllText(Path.Combine(game, "Maps/common_table.lgc"), Encoding.Latin1));
        Assert.True(File.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName, "Level_10.map")));

        RandomAddOnInstaller.Remove(game);
        foreach ((string file, byte[] bytes) in before)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(game, file))), file);
        }

        Assert.False(Directory.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName)));
    }

    private string CopyGame()
    {
        string source = TestGameFolder.RequireFolder();
        foreach (string file in Files.Concat(Enumerable.Range(1, 10).SelectMany(n => new[] { $"Maps/Level_{n:00}.map", $"Maps/Level_{n:00}.lgc" })))
        {
            string target = Path.Combine(_folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, file), target);
        }

        return _folder;
    }
}

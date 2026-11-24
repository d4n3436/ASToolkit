using System.Text;
using ASToolkit.Analysis.Reference;
using ASToolkit.Analysis.Scripting;
using ASToolkit.Generation.Patching;
using Xunit;

namespace ASToolkit.Tests.Patching;

public sealed class RandomAddOnInstallerTests : IDisposable
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
        RandomAddOnInstaller.Remove(game);
        var before = Files.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(game, f)));

        RandomAddOnInstaller.Install(game, GenerationStyle.AddOn, seed: 1, [], [700, 3000, 9000, 15000, 0]);

        string common = Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(game, "Maps/COMMON.LGC")));
        Assert.Contains("GAME_TYPE_RANDOM", common);
        Assert.Contains("#define RANDOM_LEVELS 5", File.ReadAllText(Path.Combine(game, "Maps/common_table.lgc"), Encoding.ASCII));
        Assert.Equal([100, 700, 3000, 9000, 15000], GameTable.Load(Path.Combine(game, "Maps/common_table.lgc")).GetArray("LevelMoneyRandom").Take(5));
        Assert.Contains("Addon3=GENERATED", File.ReadAllText(Path.Combine(game, "strings.ini"), Encoding.ASCII));
        Assert.Contains("Addon3", Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(game, "Maps/PlayWithAddon1.men"))));
        Assert.True(File.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName, "Level_05.map")));
        Assert.False(File.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName, "Level_06.map")));

        RandomAddOnInstaller.Install(game, GenerationStyle.Campaign, seed: 1, [], []);
        Assert.Contains("#define RANDOM_LEVELS 10", File.ReadAllText(Path.Combine(game, "Maps/common_table.lgc"), Encoding.ASCII));
        Assert.True(File.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName, "Level_10.map")));

        RandomAddOnInstaller.Remove(game);
        foreach ((string file, byte[] bytes) in before)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(game, file))), file);
        }

        Assert.False(Directory.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName)));
    }

    [Fact]
    public void InstallAndRemove_TableWithHighBytes_KeepsEveryByteAroundTheBlock()
    {
        string game = CopyGame();
        RandomAddOnInstaller.Remove(game);
        string table = Path.Combine(game, "Maps/common_table.lgc");
        byte[] high = Enumerable.Range(0x80, 0x80).Select(value => (byte)value).ToArray();
        byte[] original = Concat(File.ReadAllBytes(table), "\n//"u8.ToArray(), high, "\n"u8.ToArray());
        File.WriteAllBytes(table, original);

        RandomAddOnInstaller.Install(game, GenerationStyle.AddOn, seed: 1, [], [700, 3000, 9000, 15000, 0]);

        byte[] installed = File.ReadAllBytes(table);
        Assert.True(installed.AsSpan().StartsWith(original));
        Assert.True(installed.AsSpan(original.Length).IndexOf("#define RANDOM_LEVELS 5"u8) >= 0);

        RandomAddOnInstaller.Remove(game);

        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(table)));
    }

    [Fact]
    public void Install_PatchedMenuWithoutBackup_ThrowsAndWritesNothing()
    {
        string game = CopyGame();
        RandomAddOnInstaller.Remove(game);
        string menu = Path.Combine(game, "Maps/PlayWithAddon1.men");
        RandomAddOnInstaller.Install(game, GenerationStyle.AddOn, seed: 1, [], [700, 3000, 9000, 15000, 0]);
        File.Delete(menu + ".orig");
        RandomAddOnInstaller.Remove(game);
        byte[] table = File.ReadAllBytes(Path.Combine(game, "Maps/common_table.lgc"));

        Assert.Throws<InvalidDataException>(() => RandomAddOnInstaller.Install(game, GenerationStyle.AddOn, seed: 1, [], [700, 3000, 9000, 15000, 0]));

        Assert.False(File.Exists(menu + ".orig"));
        Assert.Equal(table, File.ReadAllBytes(Path.Combine(game, "Maps/common_table.lgc")));
        Assert.False(Directory.Exists(Path.Combine(game, "Maps", RandomAddOnInstaller.FolderName)));
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private string CopyGame()
    {
        string source = TestGameFolder.RequireFolder();
        foreach (string file in Files.Concat(Enumerable.Range(1, 10).SelectMany(n => new[] { $"Maps/Level_{n:00}.map", $"Maps/Level_{n:00}.lgc" })))
        {
            string target = Path.Combine(_folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, file), target);
            if (File.Exists(Path.Combine(source, file + ".orig")))
            {
                File.Copy(Path.Combine(source, file + ".orig"), target + ".orig");
            }
        }

        return _folder;
    }
}

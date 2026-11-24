using System.Buffers.Binary;
using ASToolkit.Generation.Widescreen;
using Xunit;

namespace ASToolkit.Tests.Widescreen;

public sealed class WidescreenInstallerTests : IDisposable
{
    private const int WidthsOffset = 0x7f5b0;
    private const int HeightsOffset = 0x7f630;
    private const int DepthsOffset = 0x7f6b0;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"astoolkit-widescreen-{Guid.NewGuid():N}");

    private static ReadOnlySpan<byte> Common => "#define FADE_GAME_TIME        1000 //fade\r\n\r\nAddCoorX(int unit,int dx)\r\n{\r\n}\r\n\r\nmain()\r\n{\r\n  SetScrollType(SCROLL_TO_FLAGMAN_WITH_DIRECTION);\r\n  if( 1 )\r\n    SetScrollType(SCROLL_TO_FLAGMAN_WITH_DIRECTION);\r\n}\r\n"u8;

    private static ReadOnlySpan<byte> Statebar => "Create()\r\n{\r\n  int i,menu;\r\n  string text;\r\n    MenuLoad(\"maps\\\\gamebar\"+itoa(ScreenX())+\".men\");\r\n        menu = MenuFind(741,999999);//hp,armor\r\n    menu = MenuFind(741,999999);//hp,armor\r\n    StateBarScores = CreateSprite(3,2,7,0);\r\n}\r\n"u8;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Install_StockGame_AddsResolutionsAndEditsScriptsThenRemoveRestoresThem()
    {
        byte[] exe = CreateExecutable();
        WriteGame(exe, Common);

        WidescreenInstaller.Install(_folder);

        byte[] patched = File.ReadAllBytes(Path.Combine(_folder, "AlienShooter.exe"));
        Assert.Equal(1280, BinaryPrimitives.ReadInt32LittleEndian(patched.AsSpan(WidthsOffset + 12)));
        Assert.Equal(720, BinaryPrimitives.ReadInt32LittleEndian(patched.AsSpan(HeightsOffset + 12)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(patched.AsSpan(WidthsOffset + 48)));
        Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(patched.AsSpan(DepthsOffset)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(patched.AsSpan(DepthsOffset + 4)));
        Assert.Equal(0xf0000000, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(0x9de7)));
        byte[] common = File.ReadAllBytes(Path.Combine(_folder, "Maps", "COMMON.LGC"));
        Assert.True(common.AsSpan().IndexOf("FADE_GAME_TIME        300"u8) >= 0);
        Assert.True(common.AsSpan().IndexOf("AddCoorY(int unit,int dy)"u8) >= 0);
        Assert.Equal(2, common.AsSpan().Count("SCROLL_FLAGMAN_IN_CENTER"u8));
        byte[] statebar = File.ReadAllBytes(Path.Combine(_folder, "Maps", "common_statebar.lgc"));
        Assert.True(statebar.AsSpan().IndexOf("gamebar"u8) < 0);
        Assert.True(statebar.AsSpan().IndexOf("        menu = MenuFind(741,999999);//hp,armor"u8) >= 0);
        Assert.Equal(1, statebar.AsSpan().Count("CreateSprite(741,60,ScreenY()+464, 500);"u8));

        WidescreenInstaller.Install(_folder);
        Assert.True(common.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_folder, "Maps", "COMMON.LGC"))));

        WidescreenInstaller.Remove(_folder);
        Assert.True(exe.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_folder, "AlienShooter.exe"))));
        Assert.True(Common.SequenceEqual(File.ReadAllBytes(Path.Combine(_folder, "Maps", "COMMON.LGC"))));
        Assert.True(Statebar.SequenceEqual(File.ReadAllBytes(Path.Combine(_folder, "Maps", "common_statebar.lgc"))));
        Assert.False(File.Exists(Path.Combine(_folder, "AlienShooter.exe.orig")));
    }

    [Fact]
    public void InstallAndRemove_ScriptsWithHighBytes_KeepEveryByteTheEditsDoNotTouch()
    {
        byte[] high = Enumerable.Range(0x80, 0x80).Select(value => (byte)value).ToArray();
        byte[] common = Concat(high, "//"u8.ToArray(), high, "\r\n"u8.ToArray(), Common.ToArray(), high);
        byte[] exe = CreateExecutable();
        WriteGame(exe, common);

        WidescreenInstaller.Install(_folder);

        byte[] patched = File.ReadAllBytes(Path.Combine(_folder, "Maps", "COMMON.LGC"));
        Assert.True(patched.AsSpan().StartsWith(common.AsSpan(0, high.Length * 2 + 4)));
        Assert.True(patched.AsSpan().EndsWith(high));
        Assert.True(patched.AsSpan().IndexOf("FADE_GAME_TIME        300"u8) >= 0);

        WidescreenInstaller.Remove(_folder);

        Assert.True(common.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_folder, "Maps", "COMMON.LGC"))));
    }

    [Fact]
    public void Install_ExecutableThatIsNotStock_Throws()
    {
        byte[] exe = CreateExecutable();
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(WidthsOffset), 1280);
        WriteGame(exe, Common);

        Assert.Throws<InvalidDataException>(() => WidescreenInstaller.Install(_folder));
    }

    private static byte[] CreateExecutable()
    {
        byte[] exe = new byte[0x80000 + 0x1000];
        int[] widths = [640, 800, 1024];
        for (int widthIndex = 0; widthIndex < widths.Length; widthIndex++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(WidthsOffset + 4 * widthIndex), widths[widthIndex]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(0x9de7), 0xef360000);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(0x9ded), 0x90ca0000);
        return exe;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private void WriteGame(byte[] exe, ReadOnlySpan<byte> common)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Maps"));
        File.WriteAllBytes(Path.Combine(_folder, "AlienShooter.exe"), exe);
        File.WriteAllBytes(Path.Combine(_folder, "Maps", "COMMON.LGC"), common);
        File.WriteAllBytes(Path.Combine(_folder, "Maps", "common_statebar.lgc"), Statebar);
    }
}

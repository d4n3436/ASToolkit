using System.Buffers.Binary;
using System.Text;
using ASToolkit.Core.Widescreen;
using Xunit;

namespace ASToolkit.Tests.Widescreen;

public class WidescreenInstallerTests : IDisposable
{
    private const int WidthsOffset = 0x7f5b0;
    private const int HeightsOffset = 0x7f630;
    private const int DepthsOffset = 0x7f6b0;
    private const string Common = "#define FADE_GAME_TIME        1000 //fade\r\n\r\nAddCoorX(int unit,int dx)\r\n{\r\n}\r\n\r\nmain()\r\n{\r\n  SetScrollType(SCROLL_TO_FLAGMAN_WITH_DIRECTION);\r\n  if( 1 )\r\n    SetScrollType(SCROLL_TO_FLAGMAN_WITH_DIRECTION);\r\n}\r\n";

    private const string Statebar = "Create()\r\n{\r\n  int i,menu;\r\n  string text;\r\n    MenuLoad(\"maps\\\\gamebar\"+itoa(ScreenX())+\".men\");\r\n        menu = MenuFind(741,999999);//hp,armor\r\n    menu = MenuFind(741,999999);//hp,armor\r\n    StateBarScores = CreateSprite(3,2,7,0);\r\n}\r\n";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"astoolkit-widescreen-{Guid.NewGuid():N}");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Install_StockGame_AddsResolutionsAndEditsScriptsThenRemoveRestoresThem()
    {
        byte[] exe = CreateExecutable();
        WriteGame(exe, Common);

        WidescreenInstaller.Install(_folder);

        byte[] patched = File.ReadAllBytes(Path.Combine(_folder, "AlienShooter.exe"));
        Assert.Equal(1280u, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(WidthsOffset + 12)));
        Assert.Equal(720u, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(HeightsOffset + 12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(WidthsOffset + 48)));
        Assert.Equal(32u, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(DepthsOffset)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(DepthsOffset + 4)));
        Assert.Equal(0xf0000000, BinaryPrimitives.ReadUInt32LittleEndian(patched.AsSpan(0x9de7)));
        string common = File.ReadAllText(Path.Combine(_folder, "Maps", "COMMON.LGC"), Encoding.Latin1);
        Assert.Contains("FADE_GAME_TIME        300", common);
        Assert.Contains("AddCoorY(int unit,int dy)", common);
        Assert.Equal(2, common.Split("SCROLL_FLAGMAN_IN_CENTER").Length - 1);
        string statebar = File.ReadAllText(Path.Combine(_folder, "Maps", "common_statebar.lgc"), Encoding.Latin1);
        Assert.DoesNotContain("gamebar", statebar);
        Assert.Contains("        menu = MenuFind(741,999999);//hp,armor", statebar);
        Assert.Single(statebar.Split("CreateSprite(741,60,ScreenY()+464, 500);").Skip(1));

        WidescreenInstaller.Install(_folder);
        Assert.Equal(common, File.ReadAllText(Path.Combine(_folder, "Maps", "COMMON.LGC"), Encoding.Latin1));

        WidescreenInstaller.Remove(_folder);
        Assert.True(exe.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(_folder, "AlienShooter.exe"))));
        Assert.Equal(Common, File.ReadAllText(Path.Combine(_folder, "Maps", "COMMON.LGC"), Encoding.Latin1));
        Assert.Equal(Statebar, File.ReadAllText(Path.Combine(_folder, "Maps", "common_statebar.lgc"), Encoding.Latin1));
        Assert.False(File.Exists(Path.Combine(_folder, "AlienShooter.exe.orig")));
    }

    [Fact]
    public void Install_ExecutableThatIsNotStock_Throws()
    {
        byte[] exe = CreateExecutable();
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(WidthsOffset), 1280);
        WriteGame(exe, Common);

        Assert.Throws<InvalidDataException>(() => WidescreenInstaller.Install(_folder));
    }

    private static byte[] CreateExecutable()
    {
        byte[] exe = new byte[0x80000 + 0x1000];
        uint[] widths = [640, 800, 1024];
        for (int widthIndex = 0; widthIndex < widths.Length; widthIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(WidthsOffset + 4 * widthIndex), widths[widthIndex]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(0x9de7), 0xef360000);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(0x9ded), 0x90ca0000);
        return exe;
    }

    private void WriteGame(byte[] exe, string common)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Maps"));
        File.WriteAllBytes(Path.Combine(_folder, "AlienShooter.exe"), exe);
        File.WriteAllText(Path.Combine(_folder, "Maps", "COMMON.LGC"), common, Encoding.Latin1);
        File.WriteAllText(Path.Combine(_folder, "Maps", "common_statebar.lgc"), Statebar, Encoding.Latin1);
    }
}

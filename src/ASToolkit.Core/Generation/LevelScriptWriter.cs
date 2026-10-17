using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the writing of level scripts.
/// </summary>
public static class LevelScriptWriter
{
    /// <summary>
    /// Returns the music definitions of an original script, or the first campaign tracks when it has none.
    /// </summary>
    public static IReadOnlyList<string> ReadMusicLines(string originalScriptPath)
    {
        if (File.Exists(originalScriptPath))
        {
            string[] lines = GameTextEncoding.Cyrillic.GetString(File.ReadAllBytes(originalScriptPath))
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.StartsWith("#define MUSIC", StringComparison.Ordinal))
                .ToArray();
            
            if (lines.Length > 0)
            {
                return lines;
            }
        }

        return ["#define MUSIC0 \"music\\\\mus01.ogg\"", "#define MUSIC1 \"music\\\\mus02.ogg\""];
    }

    /// <summary>
    /// Writes a level script.
    /// </summary>
    public static void Write(string path, LevelScript script) => File.WriteAllBytes(path, Format(script));

    /// <summary>
    /// Returns the bytes of a level script.
    /// </summary>
    public static byte[] Format(LevelScript script)
    {
        var lines = new List<string>(script.MusicLines)
        {
            "",
            "#include \"maps\\export.lgc\"",
            "#include \"maps\\common.lgc\"",
            "#include \"maps\\common_compaign.lgc\"",
            "",
            "main()",
            "{",
            "  int i;",
            "  iff(1)",
            "  {",
            "    InitGame();",
            $"    DoorsIsOpen = {(script.DoorsOpen ? 1 : 0)};",
            "  }",
            "  if( GameTact() )",
            "    return;",
            "  CompaignGameTact();"
        };
        if (script.Blackout is not null)
        {
            (int x, int y) = ((int)script.Blackout.Value.X, (int)script.Blackout.Value.Y);
            lines.AddRange(
            [
                $"  iff( SizeTo(Flagman(0),{x},{y}) < 120 )",
                "  {//blackout: lights off, doors locked until the breaker",
                "    StartPlayerX=GetX(Flagman(0)); StartPlayerY=GetY(Flagman(0)); StartPlayerZ=GetZ(Flagman(0));",
                "    DoorsIsOpen = 0;",
                "    PlaySFX(44);",
                "    EarthQuake(3000);",
                "    LightOff(0x606060);",
                "    for( i=FirstSprite(); i; i=NextSprite() )",
                $"      if( GetUnitVid(i)=={(int)KnownObject.KeypadOn} )",
                "      {",
                $"        CreateSprite({(int)KnownObject.KeypadOff},GetX(i),GetY(i),GetZ(i),GetDirection(i));",
                "        Destroy(i);",
                "      }",
                "  }"
            ]);
        }

        if (script.Checkpoint is not null)
        {
            (int x, int y) = ((int)script.Checkpoint.Value.X, (int)script.Checkpoint.Value.Y);
            lines.AddRange(
            [
                $"  iff( SizeTo(Flagman(0),{x},{y}) < 120 )",
                "  {",
                $"    StartPlayerX = {x};",
                $"    StartPlayerY = {y};",
                "    StartPlayerZ = 0;",
                "  }"
            ]);
        }

        lines.Add("}");
        lines.Add("");
        return GameTextEncoding.Cyrillic.GetBytes(string.Join("\r\n", lines));
    }
}

using System.Text;
using System.Text.RegularExpressions;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Patching;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the installation of generated levels as a third add-on next to the game's two. Its levels are
/// <c>Maps/RANDOM/Level_NN</c>, and the game's scripts and play menu get the branches that start and run it.
/// </summary>
public static class RandomAddOnInstaller
{
    /// <summary>
    /// The folder of the add-on's levels under Maps.
    /// </summary>
    public const string FolderName = "RANDOM";

    private const int CampaignLevelCount = 10;
    private const string LabelKey = "Addon3";
    private const string TableStart = "// >>> generator: random add-on";
    private const string TableEnd = "// <<< generator";

    private static readonly Encoding Latin1 = Encoding.Latin1;

    // Each edit adds a branch for game type 4 next to the add-on 2 one. An edit is undone by putting the old text back.
    private static readonly ScriptEdit[] Edits =
    [
        new("Maps/COMMON.LGC", "#define GAME_TYPE_ADDON2   3", "#define GAME_TYPE_ADDON2   3\n#define GAME_TYPE_RANDOM   4"),
        new("Maps/COMMON.LGC", "PlayerMoney += LevelMoneyAddon2[LevelNumber];",
            "PlayerMoney += LevelMoneyAddon2[LevelNumber];\n        else if ( GetReg(GetDefaultRegPath(),\"GameType\",GAME_TYPE_COMPAIGN) == GAME_TYPE_RANDOM )\n            PlayerMoney += LevelMoneyRandom[LevelNumber];"),
        new("Maps/COMMON.LGC", "if( LevelNumber == 10 )",
            "if( LevelNumber == 10 || ( GetReg(GetDefaultRegPath(),\"GameType\",GAME_TYPE_COMPAIGN) == GAME_TYPE_RANDOM && LevelNumber == RANDOM_LEVELS ) )", 2),
        new("Maps/common_fame.lgc", "    type = \"AddOn2\";",
            "    type = \"AddOn2\";\n  else if( GetReg(GetDefaultRegPath(),\"GameType\",0) == GAME_TYPE_RANDOM )\n    type = \"Random\";"),
        new("Maps/common_fame.lgc", "    save += 300;",
            "    save += 300;\n  else if( GetReg(GetDefaultRegPath(),\"GameType\",0) == GAME_TYPE_RANDOM )\n    save += 400;"),
        new("Maps/SHOP.LGC", "    else if ( GetReg(GetDefaultRegPath(),\"GameType\",0) == GAME_TYPE_ADDON2 )",
            "    else if ( GetReg(GetDefaultRegPath(),\"GameType\",0) == GAME_TYPE_RANDOM )\n        text = Printf(\"text\\\\random_level_%02i.txt\",i);\n    else if ( GetReg(GetDefaultRegPath(),\"GameType\",0) == GAME_TYPE_ADDON2 )"),
        new("Maps/SHOP.LGC", "      else if ( GetReg(GetDefaultRegPath(),\"GameType\",GAME_TYPE_COMPAIGN) == GAME_TYPE_ADDON2 )",
            "      else if ( GetReg(GetDefaultRegPath(),\"GameType\",GAME_TYPE_COMPAIGN) == GAME_TYPE_RANDOM )\n        Load( Printf(\"maps\\\\random\\\\level_%02i.map\",i) );\n      else if ( GetReg(GetDefaultRegPath(),\"GameType\",GAME_TYPE_COMPAIGN) == GAME_TYPE_ADDON2 )"),
        new("Maps/MAINMENU.LGC", "#define MENU_ADDON2   11", "#define MENU_ADDON2   11\n#define MENU_RANDOM   12"),
        new("Maps/MAINMENU.LGC", "          ChangeMenuState(MENU_ADDON2);",
            "          ChangeMenuState(MENU_ADDON2);\n        else if( MenuNDirUnderCursor()==5 )\n          ChangeMenuState(MENU_RANDOM);"),
        new("Maps/MAINMENU.LGC", "  else if( MenuState==MENU_SURVIVE )//Survive",
            "  else if( MenuState==MENU_RANDOM )//random add-on\n  {\n    if( StartEffectTime && GetEffectState(EFF_FADE) > 45 )\n    {\n      StartEffectTime = 0;\n      ResetGameParameters();\n      SetReg(GetDefaultRegPath(),\"GameType\",GAME_TYPE_RANDOM);\n      Load(\"maps\\\\shop.map\");\n    }\n  }\n  else if( MenuState==MENU_SURVIVE )//Survive"),
        new("strings.ini", "Addon=FIGHT FOR LIFE", "Addon=FIGHT FOR LIFE\nAddon3=GENERATED")
    ];

    private static readonly string[] PlayMenus = ["PlayWithAddon1.men", "PlayWithoutAddon1.men"];

    /// <summary>
    /// Writes the levels, their briefings, the money table and level count, and the branches and menu button that start
    /// the add-on. Campaign style (every level a campaign one, those not generated copied from the campaign) has ten
    /// levels, add-on style five. Anything installed before is replaced.
    /// </summary>
    public static void Install(string gameFolder, bool addOnStyle, int seed, IReadOnlyList<RandomAddOnLevel> levels)
    {
        string maps = Path.Combine(gameFolder, "Maps");
        RemoveLevels(gameFolder);
        string folder = Path.Combine(maps, FolderName);
        Directory.CreateDirectory(folder);
        int count = addOnStyle ? LevelGenerator.AddOnLevelCount : CampaignLevelCount;
        for (int number = 1; number <= count; number++)
        {
            string target = Path.Combine(folder, $"Level_{number:00}.map");
            var generated = levels.FirstOrDefault(l => l.Number == number);
            if (generated is not null)
            {
                generated.Level.Write(target, generated.TemplatePath);
                File.WriteAllText(GetBriefingPath(gameFolder, number), FormatBriefing(number, seed, generated.Level.Theme), GameTextEncoding.Cyrillic);
            }
            else
            {
                CopyCampaignLevel(gameFolder, number, target);
            }
        }

        WriteTable(maps, addOnStyle, count);
        foreach (var edit in Edits)
        {
            ScriptEditor.Apply(gameFolder, edit);
        }

        foreach (string menu in PlayMenus)
        {
            string path = Path.Combine(maps, menu);
            if (!File.Exists(path))
            {
                continue;
            }

            string backup = path + ".orig";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            File.WriteAllBytes(path, PlayMenuPatcher.Patch(File.ReadAllBytes(backup), LabelKey));
        }
    }

    /// <summary>
    /// Takes the add-on out of the game, undoing the script edits and putting the menus back, and returns what was done.
    /// </summary>
    public static IReadOnlyList<string> Remove(string gameFolder)
    {
        var done = new List<string>();
        string maps = Path.Combine(gameFolder, "Maps");
        foreach (var edit in Edits.Reverse())
        {
            if (ScriptEditor.Undo(gameFolder, edit))
            {
                done.Add($"{edit.File} edit");
            }
        }

        string table = Path.Combine(maps, "common_table.lgc");
        if (File.Exists(table))
        {
            string text = Latin1.GetString(File.ReadAllBytes(table));
            string stripped = StripTable(text);
            if (stripped != text)
            {
                File.WriteAllBytes(table, Latin1.GetBytes(stripped));
                done.Add("Maps/common_table.lgc table");
            }
        }

        foreach (string menu in PlayMenus)
        {
            string path = Path.Combine(maps, menu);
            if (File.Exists(path + ".orig"))
            {
                File.Copy(path + ".orig", path, overwrite: true);
                File.Delete(path + ".orig");
                done.Add($"Maps/{menu}");
            }
        }

        if (RemoveLevels(gameFolder))
        {
            done.Add($"Maps/{FolderName}");
        }

        return done;
    }

    private static bool RemoveLevels(string gameFolder)
    {
        bool removed = false;
        string folder = Path.Combine(gameFolder, "Maps", FolderName);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
            removed = true;
        }

        string text = Path.Combine(gameFolder, "Text");
        if (Directory.Exists(text))
        {
            foreach (string briefing in Directory.EnumerateFiles(text, "random_level_*.txt"))
            {
                File.Delete(briefing);
            }
        }

        return removed;
    }

    private static string GetBriefingPath(string gameFolder, int number) => Path.Combine(gameFolder, "Text", $"random_level_{number:00}.txt");

    private static string FormatBriefing(int number, int seed, LevelTheme theme) =>
        $"MISSION {number:00}:\r\ngenerated {(theme == LevelTheme.Lab ? "laboratory" : "military base")}\r\n\r\nDESCRIPTION:\r\nseed {seed}.\r\n";

    // A level the generator did not make is the campaign's own, with its briefing.
    private static void CopyCampaignLevel(string gameFolder, int number, string target)
    {
        string maps = Path.Combine(gameFolder, "Maps");
        string name = $"Level_{number:00}";
        foreach (string extension in new[] { ".map", ".lgc" })
        {
            string source = Path.Combine(maps, name + extension);
            File.Copy(source, Path.ChangeExtension(target, extension), overwrite: true);
        }

        string briefing = Path.Combine(gameFolder, "Text", $"level_{number:00}.txt");
        if (File.Exists(briefing))
        {
            File.Copy(briefing, GetBriefingPath(gameFolder, number), overwrite: true);
        }
    }

    // The level count and the money after each level, from the campaign's table in campaign style and add-on 1's in add-on style.
    private static void WriteTable(string maps, bool addOnStyle, int count)
    {
        string path = Path.Combine(maps, "common_table.lgc");
        string text = StripTable(Latin1.GetString(File.ReadAllBytes(path)));
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        int[] money = ReadTable(text, addOnStyle ? "LevelMoneyAddon" : "LevelMoney");
        string block = string.Join(newline, TableStart, $"#define RANDOM_LEVELS {count}", $"static int LevelMoneyRandom[] = {{ {string.Join(" ,", money)} }};", TableEnd, "");
        if (!text.EndsWith('\n'))
        {
            text += newline;
        }

        File.WriteAllBytes(path, Latin1.GetBytes(text + block));
    }

    private static string StripTable(string text)
    {
        int start = text.IndexOf(TableStart, StringComparison.Ordinal);
        int end = text.IndexOf(TableEnd, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return text;
        }

        end = text.IndexOf('\n', end);
        return text[..start] + (end < 0 ? "" : text[(end + 1)..]);
    }

    private static int[] ReadTable(string text, string name)
    {
        var match = Regex.Match(text, $@"\b{name}\[\]\s*=\s*\{{([^}}]*)\}}");
        if (!match.Success)
        {
            throw new InvalidDataException($"common_table.lgc has no {name} table.");
        }

        return match.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
    }
}

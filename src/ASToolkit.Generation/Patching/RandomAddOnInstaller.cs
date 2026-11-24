using System.Text;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Reference;
using ASToolkit.Analysis.Scripting;
using ASToolkit.Formats;

namespace ASToolkit.Generation.Patching;

/// <summary>
/// Provides methods for installing generated levels as a third add-on next to the game's two.
/// </summary>
/// <remarks>
/// The levels are stored as <c>Maps/RANDOM/Level_NN</c>, and the game's scripts and play menu get the branches that
/// start and run the add-on.
/// </remarks>
public static class RandomAddOnInstaller
{
    /// <summary>
    /// Represents the folder of the add-on's levels under Maps.
    /// </summary>
    public const string FolderName = "RANDOM";
    private const string LabelKey = "Addon3";
    private const string TableStart = "// >>> generator: random add-on";
    private const string TableEnd = "// <<< generator";

    // Add a game type 4 branch next to each add-on 2 branch
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
    /// the add-on. Campaign style has ten levels, each of them a campaign level, with the levels that weren't generated
    /// copied from the campaign; add-on style has five. Anything installed before is replaced.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="style">The style of the levels.</param>
    /// <param name="seed">The seed the levels were generated from.</param>
    /// <param name="levels">The generated levels.</param>
    /// <param name="endMoney">The money paid at the end of each level, from level 1.</param>
    public static void Install(string gameFolder, GenerationStyle style, int seed, IReadOnlyList<RandomAddOnLevel> levels, IReadOnlyList<int> endMoney)
    {
        string maps = Path.Combine(gameFolder, "Maps");
        var menus = PatchMenus(maps);
        RemoveLevels(gameFolder);
        string folder = Path.Combine(maps, FolderName);
        Directory.CreateDirectory(folder);
        int count = style.LevelCount;
        for (int number = 1; number <= count; number++)
        {
            string target = Path.Combine(folder, $"Level_{number:00}.map");
            var generated = levels.FirstOrDefault(l => l.Number == number);
            if (generated is not null)
            {
                generated.Level.Write(target, generated.TemplatePath);
                File.WriteAllText(GetBriefingPath(gameFolder, number), FormatBriefing(number, seed, generated.Level.Building), GameTextEncoding.Cyrillic);
            }
            else
            {
                CopyCampaignLevel(gameFolder, number, target);
            }
        }

        WriteTable(maps, style, count, endMoney);
        foreach (var edit in Edits)
        {
            ScriptEditor.Apply(gameFolder, edit);
        }

        foreach ((string path, byte[] patched) in menus)
        {
            if (!File.Exists(path + ".orig"))
            {
                File.Copy(path, path + ".orig");
            }

            File.WriteAllBytes(path, patched);
        }
    }

    /// <summary>
    /// Takes the add-on out of a game folder by undoing the script edits and putting the menus back.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <returns>The description of each change that was undone.</returns>
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

        string table = Path.Combine(maps, GameTable.CommonFileName);
        if (File.Exists(table))
        {
            byte[] text = File.ReadAllBytes(table);
            byte[] stripped = StripTable(text);
            if (!stripped.AsSpan().SequenceEqual(text))
            {
                File.WriteAllBytes(table, stripped);
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

    private static (string Path, byte[] Patched)[] PatchMenus(string maps)
    {
        var menus = new List<(string, byte[])>();
        foreach (string menu in PlayMenus)
        {
            string path = Path.Combine(maps, menu);
            if (File.Exists(path))
            {
                string source = File.Exists(path + ".orig") ? path + ".orig" : path;
                menus.Add((path, PlayMenuPatcher.Patch(File.ReadAllBytes(source), LabelKey)));
            }
        }

        return menus.ToArray();
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

    private static string FormatBriefing(int number, int seed, Building building) =>
        $"MISSION {number:00}:\r\ngenerated {GetBuildingName(building)}\r\n\r\nDESCRIPTION:\r\nseed {seed}.\r\n";

    private static string GetBuildingName(Building building) => building switch
    {
        Building.UndergroundLab => "underground lab",
        Building.Warehouse => "warehouse",
        Building.Prison => "prison",
        Building.OfficeBlock => "office block",
        Building.ConferenceBuilding => "conference building",
        Building.School => "school",
        Building.TrainingComplex => "training complex",
        _ => "outskirts"
    };

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

    // Pay the given money at the end of each level but the last, using a table indexed by the number of the level that ends
    private static void WriteTable(string maps, GenerationStyle style, int count, IReadOnlyList<int> endMoney)
    {
        string path = Path.Combine(maps, GameTable.CommonFileName);
        byte[] text = StripTable(File.ReadAllBytes(path));
        string newline = text.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
        int[] money = GameTable.Parse(text).GetArray(style == GenerationStyle.AddOn ? "LevelMoneyAddon" : "LevelMoney").ToArray();
        for (int level = 1; level < Math.Min(count, money.Length); level++)
        {
            money[level] = level <= endMoney.Count ? endMoney[level - 1] : money[level];
        }

        string block = string.Join(newline, TableStart, $"#define RANDOM_LEVELS {count}", $"static int LevelMoneyRandom[] = {{ {string.Join(" ,", money)} }};", TableEnd, "");
        using var file = File.Create(path);
        file.Write(text);
        if (text.Length != 0 && text[^1] != (byte)'\n')
        {
            file.Write(Encoding.ASCII.GetBytes(newline));
        }

        file.Write(Encoding.ASCII.GetBytes(block));
    }

    private static byte[] StripTable(byte[] text)
    {
        int start = text.AsSpan().IndexOf(Encoding.ASCII.GetBytes(TableStart));
        int end = text.AsSpan().IndexOf(Encoding.ASCII.GetBytes(TableEnd));
        if (start < 0 || end < start)
        {
            return text;
        }

        int lineEnd = text.AsSpan(end).IndexOf((byte)'\n');
        end = lineEnd < 0 ? -1 : end + lineEnd;

        var result = new List<byte>(text.Length);
        result.AddRange(text.AsSpan(0, start));
        if (end >= 0)
        {
            result.AddRange(text.AsSpan(end + 1));
        }

        return result.ToArray();
    }
}

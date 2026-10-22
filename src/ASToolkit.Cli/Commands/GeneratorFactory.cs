using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation;
using ASToolkit.Core.Rules;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal static class GeneratorFactory
{
    private static readonly string[] SideSources =
    [
        "Level_02.map", "Level_03.map", "Level_04.map", "Level_05.map", "Level_06.map", "Level_07.map", "Level_08.map", "Level_09.map", "Level_10.map",
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    public static (LevelGenerator Generator, ReferenceLevelSet References, ObjectTable Objects) Create(string game, bool addOnStyle = false)
    {
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = AnsiConsole.Status().Start("Analyzing the original levels", _ => ReferenceLevelSet.Load(game, objects, addOnStyle));
        var sides = SideTable.Load(game, SideSources);
        return (new LevelGenerator(game, objects, references, sides, addOnStyle), references, objects);
    }
}

using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Rules;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal static class GeneratorFactory
{
    private static readonly string[] ArmySources =
    [
        "Level_02.map", "Level_03.map", "Level_04.map", "Level_05.map", "Level_06.map", "Level_07.map", "Level_08.map", "Level_09.map", "Level_10.map",
        "ADDON/Level_01.map", "ADDON/Level_02.map", "ADDON/Level_03.map", "ADDON/Level_04.map", "ADDON/Level_05.map",
        "ADDON2/Level_01.map", "ADDON2/Level_02.map", "ADDON2/Level_03.map", "ADDON2/Level_04.map", "ADDON2/Level_05.map"
    ];

    public static (LevelGenerator Generator, ReferenceLevelSet References, ObjectTable Objects) Create(string game, bool addOnStyle = false, LayoutStyle style = LayoutStyle.Room)
    {
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = AnsiConsole.Status().Start("Analyzing the original levels", _ => ReferenceLevelSet.Load(game, objects, addOnStyle));
        var armies = ArmyTable.Load(game, ArmySources);
        return (new LevelGenerator(game, objects, references, armies, addOnStyle, style), references, objects);
    }
}

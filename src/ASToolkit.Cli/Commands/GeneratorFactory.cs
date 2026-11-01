using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation;
using ASToolkit.Core.Reference;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal static class GeneratorFactory
{
    public static (LevelGenerator Generator, ReferenceLevelSet References, ObjectTable Objects) Create(string game, bool addOnStyle = false)
    {
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = AnsiConsole.Status().Start("Analyzing the original levels", _ => ReferenceLevelSet.Load(game, objects, addOnStyle));
        return (new LevelGenerator(game, objects, references, addOnStyle), references, objects);
    }
}

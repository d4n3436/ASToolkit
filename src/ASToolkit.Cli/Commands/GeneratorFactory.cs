using ASToolkit.Formats;
using ASToolkit.Generation;
using ASToolkit.Analysis.Reference;
using ASToolkit.Analysis.Scripting;
using Spectre.Console;

namespace ASToolkit.Cli.Commands;

internal static class GeneratorFactory
{
    public static (LevelGenerator Generator, ReferenceLevelSet References, ObjectTable Objects) Create(string game, GenerationStyle style = GenerationStyle.Campaign)
    {
        var objects = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var references = AnsiConsole.Status().Start("Analyzing the original levels", _ => ReferenceLevelSet.Load(game, objects));
        return (new LevelGenerator(game, objects, references, new StyleFitter(objects, GameTable.LoadCommon(game)).Fit(references, style)), references, objects);
    }
}

using ASToolkit.Formats;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the loading and analysis of a level's map and script.
/// </summary>
public sealed class LevelLoader
{
    private readonly ObjectTable _objects;

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelLoader"/> class.
    /// </summary>
    /// <param name="objects">The object table.</param>
    public LevelLoader(ObjectTable objects)
    {
        _objects = objects;
    }

    /// <summary>
    /// Loads a map and the level script beside it into a level.
    /// </summary>
    /// <param name="name">The map name relative to the Maps folder.</param>
    /// <param name="mapPath">The path of the map file.</param>
    /// <param name="isOriginal">Whether the map is one of the game's own levels.</param>
    /// <returns>The level, or null if the map doesn't have a route.</returns>
    public ReferenceLevel? Load(string name, string mapPath, bool isOriginal = true)
    {
        var map = MapResourceBuilder.Load(mapPath);
        var script = LevelScriptReader.Read(Path.ChangeExtension(mapPath, ".lgc"), _objects.ScriptVersion);
        var analysis = RouteAnalyzer.Analyze(name, map, script, _objects);
        if (analysis is null)
        {
            return null;
        }

        var measures = LevelMeasurer.Measure(map, script, analysis, _objects);
        var survey = LevelSurveyor.Survey(map, script, analysis, _objects, measures);
        return new ReferenceLevel
        {
            Name = name,
            Map = map,
            Script = script,
            Analysis = analysis,
            Measures = measures,
            Model = analysis.Model,
            Survey = survey,
            Atlas = RoomAtlas.Read(survey, analysis.Grid.Edges, map.Sprites, _objects, isOriginal)
        };
    }
}

using ASToolkit.Core.Formats;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Provides the loading of a level's map and script with their analysis, measures, survey and rooms.
/// </summary>
public sealed class LevelLoader
{
    private readonly ObjectTable _objects;

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelLoader"/> class.
    /// </summary>
    public LevelLoader(ObjectTable objects)
    {
        _objects = objects;
    }

    /// <summary>
    /// Loads a map and the level script beside it into a level, or returns null when the map has no route.
    /// </summary>
    public ReferenceLevel? Load(string name, string mapPath)
    {
        var map = MapResourceBuilder.Load(mapPath);
        var script = LevelScriptReader.Read(Path.ChangeExtension(mapPath, ".lgc"));
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
            Atlas = RoomAtlas.Read(survey, analysis.Grid.Edges, map.Sprites, _objects)
        };
    }
}

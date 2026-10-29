using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents everything the rules read about one level.
/// </summary>
public sealed class RuleContext
{
    /// <summary>
    /// Gets the map.
    /// </summary>
    public required MapResourceBuilder Map { get; init; }

    /// <summary>
    /// Gets the object table of the game.
    /// </summary>
    public required ObjectTable Objects { get; init; }

    /// <summary>
    /// Gets the cell model of the map, or null when it has no floor.
    /// </summary>
    public required LevelModel? Model { get; init; }

    /// <summary>
    /// Gets the level script.
    /// </summary>
    public required LevelScriptSummary Script { get; init; }

    /// <summary>
    /// Gets the route analysis.
    /// </summary>
    public required RouteAnalysis Analysis { get; init; }

    /// <summary>
    /// Gets the measurements.
    /// </summary>
    public required LevelMeasurement Measurement { get; init; }

    /// <summary>
    /// Gets the targets.
    /// </summary>
    public required LevelTarget Target { get; init; }

    /// <summary>
    /// Measures a level and returns its context.
    /// </summary>
    public static RuleContext Create(MapResourceBuilder map, LevelScriptSummary script, RouteAnalysis analysis, ObjectTable objects, LevelTarget target)
    {
        var ordered = new MapResourceBuilder { Size = map.Size, Scroll = map.Scroll, Graphics = map.Graphics };
        ordered.Sprites.AddRange(LevelExtractor.OrderSprites(map.Sprites));
        return new RuleContext
        {
            Map = ordered,
            Objects = objects,
            Model = LevelExtractor.Extract(analysis.Name, ordered, objects),
            Script = script,
            Analysis = analysis,
            Measurement = LevelMeasurer.Measure(ordered, script, analysis, objects),
            Target = target
        };
    }
}

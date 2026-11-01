using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Survey;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents one of the game's own levels with its analysis.
/// </summary>
public sealed class ReferenceLevel
{
    /// <summary>Gets the map name relative to the Maps folder.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the map of the level.</summary>
    public required MapResourceBuilder Map { get; init; }

    /// <summary>Gets the level script of the level.</summary>
    public required LevelScriptSummary Script { get; init; }

    /// <summary>Gets the route analysis of the level.</summary>
    public required RouteAnalysis Analysis { get; init; }

    /// <summary>Gets the measurements of the level besides its tour.</summary>
    public required LevelMeasurement Measures { get; init; }

    /// <summary>Gets the model of the level.</summary>
    public required LevelModel Model { get; init; }

    /// <summary>Gets the survey of the level.</summary>
    public required LevelSurvey Survey { get; init; }

    /// <summary>Gets the rooms of the level with their roles and types.</summary>
    public required RoomAtlas Atlas { get; init; }
}

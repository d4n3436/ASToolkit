using ASToolkit.Formats;
using ASToolkit.Generation.Content;
using ASToolkit.Generation.Geometry;
using ASToolkit.Generation.Layout;
using ASToolkit.Generation.Output;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Routes;

namespace ASToolkit.Generation;

/// <summary>
/// Represents a generated level ready to be written.
/// </summary>
public sealed class GeneratedLevel
{
    /// <summary>
    /// Gets the campaign level number.
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Gets the map.
    /// </summary>
    public required MapResourceBuilder Map { get; init; }

    /// <summary>
    /// Gets or sets the original map, relative to the Maps folder, that the level is written over.
    /// </summary>
    public required string Template { get; set; }

    /// <summary>
    /// Gets the kind of building the generated level is.
    /// </summary>
    public required Building Building { get; init; }

    /// <summary>
    /// Gets the wall set of the generated level.
    /// </summary>
    public required WallSet WallSet { get; init; }

    /// <summary>
    /// Gets or sets the number of the add-on level this level replaces, or null for a campaign level.
    /// </summary>
    public int? AddOnNumber { get; set; }

    /// <summary>
    /// Gets the script.
    /// </summary>
    public required LevelScript Script { get; init; }

    /// <summary>
    /// Gets the layout plan, or null for an outdoor level.
    /// </summary>
    public LayoutPlan? Plan { get; init; }

    /// <summary>
    /// Gets the layout on the cell lattice, or null for an outdoor level.
    /// </summary>
    public FineLayout? Layout { get; init; }

    /// <summary>
    /// Gets the content budget, or null for an outdoor level.
    /// </summary>
    public LevelBudget? Budget { get; init; }

    /// <summary>
    /// Gets the standing groups and ambushes in the level's plan, which is empty for an outdoor level.
    /// </summary>
    public IReadOnlyList<PlannedFight> Fights { get; init; } = [];

    /// <summary>
    /// Gets or sets the route analysis of the level, or null when the level is not analyzed.
    /// </summary>
    public RouteAnalysis? Analysis { get; set; }

    /// <summary>
    /// Gets the number of sprites of the level from each source.
    /// </summary>
    public required IReadOnlyDictionary<SpriteSource, int> Provenance { get; init; }

    /// <summary>
    /// Gets the counts of the fallbacks and built parts of the building and doors stages.
    /// </summary>
    public required IReadOnlyDictionary<string, int> LayoutCounts { get; init; }

    /// <summary>
    /// Writes the map and its script next to it, and takes the other map chunks from the template map.
    /// </summary>
    /// <param name="mapPath">The path of the map file to write.</param>
    /// <param name="templatePath">The path of the template map.</param>
    public void Write(string mapPath, string templatePath)
    {
        Map.Write(mapPath, templatePath);
        LevelScriptWriter.Write(Path.ChangeExtension(mapPath, ".lgc"), Script);
    }
}

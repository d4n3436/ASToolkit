using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Content;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Generation;

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
    /// Gets the lattice of the floor cells.
    /// </summary>
    public required Lattice Lattice { get; init; }

    /// <summary>
    /// Gets the theme.
    /// </summary>
    public required LevelTheme Theme { get; init; }

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
    /// Gets or sets the route analysis of the level, or null when the level is not analyzed.
    /// </summary>
    public RouteAnalysis? Analysis { get; set; }

    /// <summary>
    /// Writes the map and its script next to it, taking the other map chunks from a template map.
    /// </summary>
    public void Write(string mapPath, string templatePath)
    {
        Map.Write(mapPath, templatePath);
        LevelScriptWriter.Write(Path.ChangeExtension(mapPath, ".lgc"), Script);
    }
}

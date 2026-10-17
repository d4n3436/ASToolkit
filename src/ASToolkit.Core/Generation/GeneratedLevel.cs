using ASToolkit.Core.Formats;
using ASToolkit.Core.Generation.Content;
using ASToolkit.Core.Generation.Geometry;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Rules;

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
    /// Gets or sets the map.
    /// </summary>
    public required GameMap Map { get; set; }

    /// <summary>
    /// Gets the builder that holds the map's objects.
    /// </summary>
    public required MapBuilder Builder { get; init; }

    /// <summary>
    /// Gets the floor cells outside the closets.
    /// </summary>
    public required IReadOnlySet<Cell> Shown { get; init; }

    /// <summary>
    /// Gets the theme.
    /// </summary>
    public required LevelTheme Theme { get; init; }

    /// <summary>
    /// Gets or sets the number of the add-on level this replaces, or null for a campaign level.
    /// </summary>
    public int? AddOnNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of pixels the roof should hide but cannot.
    /// </summary>
    public int RoofResidual { get; set; }

    /// <summary>
    /// Gets the script.
    /// </summary>
    public required LevelScript Script { get; init; }

    /// <summary>
    /// Gets the layout plan.
    /// </summary>
    public required LayoutPlan Plan { get; init; }

    /// <summary>
    /// Gets the layout on the cell lattice.
    /// </summary>
    public required FineLayout Layout { get; init; }

    /// <summary>
    /// Gets the content budget.
    /// </summary>
    public required LevelBudget Budget { get; init; }

    /// <summary>
    /// Gets or sets the route analysis of the level, once checked.
    /// </summary>
    public RouteAnalysis? Analysis { get; set; }

    /// <summary>
    /// Gets or sets the rule results of the level, once checked.
    /// </summary>
    public IReadOnlyList<RuleResult> Results { get; set; } = [];

    /// <summary>
    /// Writes the map and its script next to it, taking the other map chunks from a template map.
    /// </summary>
    public void Write(string mapPath, string templatePath)
    {
        Map.Write(mapPath, templatePath);
        LevelScriptWriter.Write(Path.ChangeExtension(mapPath, ".lgc"), Script);
    }
}

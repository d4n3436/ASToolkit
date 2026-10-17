using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents a connection between two areas of a layout.
/// </summary>
public sealed class PlanLink
{
    /// <summary>
    /// Gets the region nearer the start.
    /// </summary>
    public required int From { get; init; }

    /// <summary>
    /// Gets the region farther from the start.
    /// </summary>
    public required int To { get; init; }

    /// <summary>
    /// Gets or sets how the regions connect.
    /// </summary>
    public required LinkKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the unit edges that are open.
    /// </summary>
    public required List<EdgeKey> Edges { get; set; }

    /// <summary>
    /// Gets the whole common boundary.
    /// </summary>
    public required EdgeKey[] Boundary { get; init; }
}

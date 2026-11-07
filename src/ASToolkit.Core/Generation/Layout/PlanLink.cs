using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents a connection between two areas of a layout.
/// </summary>
public sealed class PlanLink
{
    /// <summary>
    /// Gets the ID of the link, its index in the plan's links.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the region nearer the start.
    /// </summary>
    public required int From { get; init; }

    /// <summary>
    /// Gets the region farther from the start.
    /// </summary>
    public required int To { get; init; }

    /// <summary>
    /// Gets how the regions connect.
    /// </summary>
    public required LinkKind Kind { get; init; }

    /// <summary>
    /// Gets the unit edges that are open.
    /// </summary>
    public required IReadOnlyList<EdgeKey> Edges { get; init; }

    /// <summary>
    /// Gets the whole common boundary.
    /// </summary>
    public required EdgeKey[] Boundary { get; init; }
}

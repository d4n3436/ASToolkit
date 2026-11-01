namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a connection between two places across a run of grid edges.
/// </summary>
public sealed class PlaceConnection
{
    /// <summary>
    /// Gets the connection number, from 1.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the lower place number.
    /// </summary>
    public required int A { get; init; }

    /// <summary>
    /// Gets the higher place number.
    /// </summary>
    public required int B { get; init; }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public required ConnectionKind Kind { get; init; }

    /// <summary>
    /// Gets the grid edges the connection crosses.
    /// </summary>
    public required IReadOnlyList<EdgeRun> Edges { get; init; }
}

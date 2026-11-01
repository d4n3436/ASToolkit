using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents an object that another object stands by in the original levels far more often than chance would put it there.
/// </summary>
public sealed class ObjectAnchor
{
    /// <summary>
    /// Gets the ID of the anchor object.
    /// </summary>
    public required KnownObject Id { get; init; }

    /// <summary>
    /// Gets the share of the objects that stand within reach of the anchor object.
    /// </summary>
    public required float Share { get; init; }

    /// <summary>
    /// Gets how many times the anchor's share exceeds its share around random floor points.
    /// </summary>
    public required float Lift { get; init; }

    /// <summary>
    /// Gets how each object stood by its nearest anchor object.
    /// </summary>
    public required IReadOnlyList<AnchorSample> Samples { get; init; }
}

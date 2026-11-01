using System.Drawing;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a landmark of a level.
/// </summary>
public sealed class LevelLandmark
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public required LandmarkKind Kind { get; init; }

    /// <summary>
    /// Gets the place it lies in.
    /// </summary>
    public required int Place { get; init; }

    /// <summary>
    /// Gets how far along the route, in map pixels, the route reaches it.
    /// </summary>
    public required int Route { get; init; }

    /// <summary>
    /// Gets the position in map pixels.
    /// </summary>
    public required Point Position { get; init; }

    /// <summary>
    /// Gets the measures around it by name.
    /// </summary>
    public required IReadOnlyDictionary<string, int> Facts { get; init; }
}

using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents the opening of a connection between two places on the map.
/// </summary>
/// <param name="Span">The segment the opening spans.</param>
/// <param name="Kind">The kind of the connection.</param>
public readonly record struct Doorway(MapSegment Span, ConnectionKind Kind);

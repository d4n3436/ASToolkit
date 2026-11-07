using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents a group of furniture that stands together in a room, such as a desk with its chair and computer or a row of cabinets.
/// </summary>
public sealed class RoomPart
{
    /// <summary>
    /// Gets where the part stands.
    /// </summary>
    public required PartSpot Spot { get; init; }

    /// <summary>
    /// Gets the kinds of furniture in the part, in order.
    /// </summary>
    public required IReadOnlyList<PieceFamily> Families { get; init; }

    /// <summary>
    /// Gets the map and place of the room the part stands in.
    /// </summary>
    public required (string Map, int Place) Room { get; init; }

    /// <summary>
    /// Gets the map position of the part's first piece.
    /// </summary>
    public required PointF Origin { get; init; }

    /// <summary>
    /// Gets the pieces of the part.
    /// </summary>
    public required IReadOnlyList<PartPiece> Pieces { get; init; }

    /// <summary>
    /// Gets the number of pieces of each object ID in the part.
    /// </summary>
    public IReadOnlyDictionary<KnownObject, int> PieceCounts => field ??= Pieces.CountBy(piece => piece.Id).ToDictionary();

    /// <summary>
    /// Gets the name of the part's kind, its families.
    /// </summary>
    public string Kind => string.Join("+", Families.Select(family => family.ToString().ToLowerInvariant()));
}

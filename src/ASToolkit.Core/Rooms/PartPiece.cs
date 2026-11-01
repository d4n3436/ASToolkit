using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents a piece of a part, placed relative to the part's first piece.
/// </summary>
/// <param name="Id">The object.</param>
/// <param name="X">The map x offset from the first piece.</param>
/// <param name="Y">The map y offset from the first piece.</param>
/// <param name="Z">The height.</param>
/// <param name="Facing">The facing of the sprite.</param>
/// <param name="Payload">The payload bytes of the sprite.</param>
/// <param name="Gaps">The piece's distances to the walls of its room.</param>
public readonly record struct PartPiece(KnownObject Id, float X, float Y, float Z, byte Facing, byte[] Payload, WallGaps Gaps);

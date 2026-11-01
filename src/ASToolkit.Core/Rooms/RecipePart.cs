namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents one kind of part of a room type's recipe, measured over the original rooms of that type.
/// </summary>
public sealed class RecipePart
{
    /// <summary>
    /// Gets the share of the parts that stand at each spot.
    /// </summary>
    public required IReadOnlyDictionary<PartSpot, float> SpotShares { get; init; }

    /// <summary>
    /// Gets the kinds of furniture in the part.
    /// </summary>
    public required IReadOnlyList<PieceFamily> Families { get; init; }

    /// <summary>
    /// Gets the number of parts of this kind per 100,000 square map pixels of room floor.
    /// </summary>
    public required float Rate { get; init; }

    /// <summary>
    /// Gets the share of the rooms of the type that hold one.
    /// </summary>
    public required float RoomShare { get; init; }

    /// <summary>
    /// Gets the median distance in map pixels between a part of this kind and the nearest other one in the same room, or none when no room
    /// holds two.
    /// </summary>
    public required float? Spacing { get; init; }

    /// <summary>
    /// Gets the share of the parts of this kind whose nearest one in the same room stands along a lattice axis from it.
    /// </summary>
    public required float RowShare { get; init; }

    /// <summary>
    /// Gets the parts of this kind in the original rooms, to place from.
    /// </summary>
    public required IReadOnlyList<RoomPart> Examples { get; init; }

    /// <summary>
    /// Gets a value indicating whether every room of the type should hold one.
    /// </summary>
    public bool IsRequired => RoomShare >= RoomRecipe.RequiredShare;

    /// <summary>
    /// Gets the name of the part's kind, its families.
    /// </summary>
    public string Kind => Examples[0].Kind;
}

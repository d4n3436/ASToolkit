namespace ASToolkit.Core.Design;

/// <summary>
/// Represents how a level's places form one building.
/// </summary>
public sealed record LevelArchitecture
{
    /// <summary>
    /// Gets the share of the level's bounding box its place cells cover.
    /// </summary>
    public required float Compactness { get; init; }

    /// <summary>
    /// Gets the wall lines of three or more cells per 1,000 place cells, counting each grid line once.
    /// </summary>
    public required float WallLinesPerThousandCells { get; init; }

    /// <summary>
    /// Gets the share of the floor in corridors.
    /// </summary>
    public required float CorridorShare { get; init; }

    /// <summary>
    /// Gets the median hall area in square pixels, or null without halls.
    /// </summary>
    public required float? HallArea { get; init; }

    /// <summary>
    /// Gets the median room area in square pixels, or null without rooms.
    /// </summary>
    public required float? RoomArea { get; init; }

    /// <summary>
    /// Gets the median closet and nook area in square pixels, or null without them.
    /// </summary>
    public required float? ClosetArea { get; init; }

    /// <summary>
    /// Gets the inner corners of the floor's outline per side.
    /// </summary>
    public required float NotchesPerSide { get; init; }
}

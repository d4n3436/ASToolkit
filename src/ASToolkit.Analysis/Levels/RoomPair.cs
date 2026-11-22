namespace ASToolkit.Analysis.Levels;

/// <summary>
/// Represents two rooms joined without a direction, the lower room ID first.
/// </summary>
public readonly record struct RoomPair : IComparable<RoomPair>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomPair"/> structure from two rooms in either order.
    /// </summary>
    /// <param name="a">The ID of a room.</param>
    /// <param name="b">The ID of the other room.</param>
    public RoomPair(int a, int b) => (A, B) = a < b ? (a, b) : (b, a);

    /// <summary>
    /// Gets the lower room ID of the pair.
    /// </summary>
    public int A { get; }

    /// <summary>
    /// Gets the higher room ID of the pair.
    /// </summary>
    public int B { get; }

    /// <summary>
    /// Determines whether a room is one of the pair.
    /// </summary>
    /// <param name="room">The room ID.</param>
    /// <returns><see langword="true"/> if the room is one of the pair; otherwise, <see langword="false"/>.</returns>
    public bool Contains(int room) => A == room || B == room;

    /// <summary>
    /// Compares the pair with another pair by the lower room and then by the higher room.
    /// </summary>
    /// <param name="other">The other pair.</param>
    /// <returns>A negative number if the pair comes before the other pair, zero if they are equal, or a positive number if it comes after.</returns>
    public int CompareTo(RoomPair other) => A != other.A ? A.CompareTo(other.A) : B.CompareTo(other.B);

    /// <summary>
    /// Deconstructs the pair into its two rooms, the lower ID first.
    /// </summary>
    /// <param name="a">The lower room ID.</param>
    /// <param name="b">The higher room ID.</param>
    public void Deconstruct(out int a, out int b) => (a, b) = (A, B);
}

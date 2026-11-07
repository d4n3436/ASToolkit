namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents two rooms joined without a direction, the lower room ID first.
/// </summary>
public readonly record struct RoomPair : IComparable<RoomPair>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomPair"/> struct from two rooms in either order.
    /// </summary>
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
    /// Returns whether a room is one of the pair.
    /// </summary>
    public bool Contains(int room) => A == room || B == room;

    /// <summary>
    /// Returns the order of the pair against another, by the lower room and then the higher.
    /// </summary>
    public int CompareTo(RoomPair other) => A != other.A ? A.CompareTo(other.A) : B.CompareTo(other.B);

    /// <summary>
    /// Returns the two rooms of the pair, the lower ID first.
    /// </summary>
    public void Deconstruct(out int a, out int b) => (a, b) = (A, B);
}

using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Buildings;

internal sealed class LayoutProblem<T>
    where T : notnull
{
    // Holds the distance to keep between rooms that aren't joined, touching or allowed to touch
    public int MinimumDistance { get; init; }

    // Holds the flush wall length shared by the touch pairs that don't have a length of their own
    public int TouchLength { get; init; } = 3;

    // Holds how far beyond the door end of an anchor the rooms past it stand, unless they open onto it
    public int BeyondMargin { get; init; }

    public Dictionary<T, (bool IsCorridor, IReadOnlyList<RoomShape> Shapes)> Rooms { get; } = [];

    public List<(T A, T B)> Joins { get; } = [];

    // Holds the sides of a room's shapes, in the frame of the shapes, that its join with a neighbor may open on
    public Dictionary<(T Room, T Neighbor), DoorSides> DoorSides { get; } = [];

    public List<(T A, T B)> TouchPairs { get; } = [];

    public Dictionary<(T A, T B), int> TouchLengths { get; } = [];

    // Holds the pairs that may share a wall instead of keeping the minimum distance, and that never stand one unit apart
    public List<(T A, T B)> MayTouchPairs { get; } = [];

    // Holds the rooms that stand past an anchor's door end and the minimum distance from its obstacles
    public List<(T Anchor, T Room)> BeyondPairs { get; } = [];

    // Holds the rooms that keep only the minimum distance from an anchor's obstacles
    public List<(T Anchor, T Room)> ClearPairs { get; } = [];

    // Holds the rectangles, in the frame of an unturned anchor, that the rooms past it or clear of it keep away from
    public List<(T Anchor, IReadOnlyList<UnitRect> Rects)> Obstacles { get; } = [];

    public void AddRoom(T room, IReadOnlyList<RoomShape> shapes, bool isCorridor = false) => Rooms.Add(room, (isCorridor, shapes));

    public void Join(T a, T b) => Joins.Add((a, b));
}

internal sealed record PlacedRoom<T>(T Room, ShapeTurn Turn, HashSet<Cell> Cells, bool IsCorridor);

using System.Drawing;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

internal sealed class LayoutProblem<T>
    where T : notnull
{
    // Holds the distance rooms not joined, touching or allowed to touch keep apart
    public int MinimumDistance { get; init; }

    // Holds the length of the flush wall touch pairs without a length of their own share
    public int TouchLength { get; init; } = 3;

    // Holds how far rooms past an anchor stand beyond its door end unless they open onto it
    public int BeyondMargin { get; init; }

    public Dictionary<T, (bool IsCorridor, IReadOnlyList<RoomShape> Shapes)> Rooms { get; } = [];

    public List<(T A, T B)> Joins { get; } = [];

    // Holds the sides of a room's shapes, in their own frame, its join with a neighbor may open on
    public Dictionary<(T Room, T Neighbor), DoorSides> DoorSides { get; } = [];

    public List<(T A, T B)> TouchPairs { get; } = [];

    public Dictionary<(T A, T B), int> TouchLengths { get; } = [];

    // Holds the pairs that may share a wall instead of keeping the minimum distance, never standing one unit apart
    public List<(T A, T B)> MayTouchPairs { get; } = [];

    // Holds the rooms that stand past an anchor's door end and the minimum distance from its obstacles
    public List<(T Anchor, T Room)> BeyondPairs { get; } = [];

    // Holds the rooms that keep only the minimum distance from an anchor's obstacles
    public List<(T Anchor, T Room)> ClearPairs { get; } = [];

    // Holds rectangles in an unturned anchor's own frame that rooms past it or clear of it keep away from
    public List<(T Anchor, IReadOnlyList<UnitRect> Rects)> Obstacles { get; } = [];

    public bool SavesPhases { get; init; }

    // Holds how many rooms of the may-touch pairs every room shares a wall at least five units long with
    public int MinContacts { get; init; }

    // Holds a starting place for rooms, unturned, in the first room's frame
    public Dictionary<T, Point> Hints { get; } = [];

    // Holds a band of the screen, this wide across (u + v) and this tall down it (v - u), that every room stays inside
    public (int Across, int Down)? Band { get; init; }

    public void AddRoom(T room, IReadOnlyList<RoomShape> shapes, bool isCorridor = false) => Rooms.Add(room, (isCorridor, shapes));

    public void Join(T a, T b) => Joins.Add((a, b));
}

internal sealed record PlacedRoom<T>(T Room, ShapeTurn Turn, HashSet<Cell> Cells, bool IsCorridor);

namespace ASToolkit.Core.Generation.Layout;

internal enum BoxKind
{
    Room,
    Neck,
    Reserve,
    Corridor,
    Court
}

internal enum Heading
{
    North,
    East,
    South,
    West
}

internal enum ExitSide
{
    Far,
    Left,
    Right
}

internal readonly record struct Box(int X0, int Y0, int X1, int Y1, BoxKind Kind, int Room)
{
    public int Width => X1 - X0;

    public int Height => Y1 - Y0;

    public bool IsSolid => Kind is BoxKind.Room or BoxKind.Neck or BoxKind.Corridor;

    public bool Overlaps(Box other) => X0 < other.X1 && other.X0 < X1 && Y0 < other.Y1 && other.Y0 < Y1;

    public int GetContact(Box other)
    {
        if (X1 == other.X0 || other.X1 == X0)
        {
            return Math.Min(Y1, other.Y1) - Math.Max(Y0, other.Y0);
        }

        if (Y1 == other.Y0 || other.Y1 == Y0)
        {
            return Math.Min(X1, other.X1) - Math.Max(X0, other.X0);
        }

        return 0;
    }

    public Box Shift(int dx, int dy) => this with { X0 = X0 + dx, Y0 = Y0 + dy, X1 = X1 + dx, Y1 = Y1 + dy };

    // Map from a frame entered across y = 0 and growing along +y into the level, turned to face the heading
    public Box ToGlobal(Heading heading, int ox, int oy)
    {
        (int a, int b, int c, int d) = heading switch
        {
            Heading.North => (ox + X0, oy + Y0, ox + X1, oy + Y1),
            Heading.East => (ox + Y0, oy - X1, ox + Y1, oy - X0),
            Heading.South => (ox - X1, oy - Y1, ox - X0, oy - Y0),
            _ => (ox - Y1, oy + X0, ox - Y0, oy + X1)
        };
        return this with { X0 = Math.Min(a, c), Y0 = Math.Min(b, d), X1 = Math.Max(a, c), Y1 = Math.Max(b, d) };
    }
}

internal sealed class RoomPacker
{
    private readonly List<Box> _placed = [];

    public IReadOnlyList<Box> Placed => _placed;

    public static Heading Turn(Heading heading, ExitSide side) => (Heading)(((int)heading + side switch
    {
        ExitSide.Right => 1,
        ExitSide.Left => 3,
        _ => 0
    }) % 4);

    public void Add(Box box) => _placed.Add(box);

    public void RemoveReserve(int child) => _placed.RemoveAll(box => box is { Kind: BoxKind.Reserve } && box.Room == child);

    public bool IsClear(IReadOnlyList<Box> moved, int ownReserve)
    {
        foreach (var box in moved)
        {
            foreach (var other in _placed)
            {
                if (box.Overlaps(other) && !(other.Kind == BoxKind.Reserve && other.Room == ownReserve))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool IsClearOfBuilt(Box box) => !_placed.Any(other => (other.IsSolid || other.Kind == BoxKind.Court) && box.Overlaps(other));

    public (int X0, int Y0, int X1, int Y1)? GetBounds()
    {
        var shown = _placed.Where(box => box.Kind is BoxKind.Room or BoxKind.Corridor).ToArray();
        return shown.Length == 0 ? null : (shown.Min(box => box.X0), shown.Min(box => box.Y0), shown.Max(box => box.X1), shown.Max(box => box.Y1));
    }
}

using System.Drawing;

namespace ASToolkit.Generation.Buildings;

internal readonly record struct DoorLine(int At, int Low, int High, int Length, int Facing, int OwnFacing) : IComparable<DoorLine>
{
    public bool IsAlongV => Facing < 2;

    public bool Faces(DoorLine other) => Facing == (other.Facing ^ 1) && Length == other.Length;

    public DoorLine Offset(Point at) => IsAlongV
        ? this with { At = At + at.X, Low = Low + at.Y, High = High + at.Y }
        : this with { At = At + at.Y, Low = Low + at.X, High = High + at.X };

    public int CompareTo(DoorLine other) =>
        (At, Low, High, Length, Facing, OwnFacing).CompareTo((other.At, other.Low, other.High, other.Length, other.Facing, other.OwnFacing));
}

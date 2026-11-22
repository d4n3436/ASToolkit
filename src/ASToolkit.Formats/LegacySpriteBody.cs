using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal readonly struct LegacySpriteBody
{
    [FieldOffset(0)]
    public readonly int Id;

    [FieldOffset(4)]
    public readonly int X;

    [FieldOffset(8)]
    public readonly int Y;

    [FieldOffset(12)]
    public readonly int Z;

    [FieldOffset(16)]
    public readonly int Direction;

    [FieldOffset(20)]
    public readonly int Army;

    public Vector3 Position => new(X, Y, Z);
}

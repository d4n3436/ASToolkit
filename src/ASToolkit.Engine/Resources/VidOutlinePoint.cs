using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

[StructLayout(LayoutKind.Explicit, Size = 6)]
internal readonly struct VidOutlinePoint
{
    [FieldOffset(0)]
    public readonly short X;

    [FieldOffset(2)]
    public readonly short Y;

    [FieldOffset(4)]
    public readonly short Z;

    public VidOutlinePoint(short x, short y, short z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal readonly struct SpriteBody
{
    [FieldOffset(0)]
    public readonly int Id;

    [FieldOffset(4)]
    public readonly Vector3 Position;

    [FieldOffset(16)]
    public readonly int Direction;

    [FieldOffset(20)]
    public readonly int Army;
}

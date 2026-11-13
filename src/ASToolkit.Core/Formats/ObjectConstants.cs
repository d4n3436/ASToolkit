using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly struct ObjectConstants
{
    [FieldOffset(8)]
    public readonly float Gravity;

    [FieldOffset(12)]
    public readonly float HeavyGravity;
}

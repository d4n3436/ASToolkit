using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 8)]
internal readonly struct DemoHeader
{
    public DemoHeader(int seed, int time)
    {
        Seed = seed;
        Time = time;
    }

    [FieldOffset(0)]
    public readonly int Seed;

    [FieldOffset(4)]
    public readonly int Time;
}

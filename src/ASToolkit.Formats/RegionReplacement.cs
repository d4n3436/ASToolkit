using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

[StructLayout(LayoutKind.Explicit, Size = 8)]
internal readonly struct RegionReplacement
{
    public RegionReplacement(int from, int to)
    {
        From = from;
        To = to;
    }

    [FieldOffset(0)]
    public readonly int From;

    [FieldOffset(4)]
    public readonly int To;
}

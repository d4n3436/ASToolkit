using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal readonly record struct FrameRows(int Position, int Top, int Count)
{
    public static FrameRows Read(ReadOnlySpan<byte> frames, int record)
    {
        int position = record + MemoryMarshal.AsRef<VidFrameHeader>(frames[record..]).Size;
        ref readonly var header = ref MemoryMarshal.AsRef<VidRowsHeader>(frames[position..]);
        return new FrameRows(position + Unsafe.SizeOf<VidRowsHeader>(), header.First, header.Count);
    }
}

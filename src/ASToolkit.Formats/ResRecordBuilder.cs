using System.Buffers;
using System.Runtime.InteropServices;
using ASToolkit.Core;

namespace ASToolkit.Formats;

internal sealed class ResRecordBuilder
{
    private readonly ArrayBufferWriter<byte> _bytes = new();

    public int CompressionSavings { get; private set; }

    public ReadOnlySpan<byte> Bytes => _bytes.WrittenSpan;

    public void Write(ReadOnlySpan<byte> data) => _bytes.Write(data);

    public void Write<T>(T value) where T : unmanaged => _bytes.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    [EngineRoutine(0x00407ca0)]
    public int WriteBody(ReadOnlySpan<byte> data, RangeCoder? coder)
    {
        if (data.IsEmpty)
            return 0;

        if (coder is null)
        {
            _bytes.Write(data);
            return data.Length;
        }

        int written = coder.Encode(data, _bytes);
        CompressionSavings += data.Length - written;
        return 0;
    }
}

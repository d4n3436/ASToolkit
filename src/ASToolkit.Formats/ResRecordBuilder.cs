using System.Buffers;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Provides a buffer that builds the bytes of a RES record.
/// </summary>
public sealed class ResRecordBuilder
{
    private readonly ArrayBufferWriter<byte> _bytes = new();

    /// <summary>
    /// Gets the number of bytes the range coder saved over the bodies written so far.
    /// </summary>
    public int CompressionSavings { get; private set; }

    /// <summary>
    /// Gets the bytes of the record written so far.
    /// </summary>
    public ReadOnlySpan<byte> Bytes => _bytes.WrittenSpan;

    /// <summary>
    /// Writes bytes to the record.
    /// </summary>
    /// <param name="data">The bytes to write.</param>
    public void Write(ReadOnlySpan<byte> data) => _bytes.Write(data);

    /// <summary>
    /// Writes the bytes of a value to the record.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to write.</param>
    public void Write<T>(T value) where T : unmanaged => _bytes.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    /// <summary>
    /// Writes a body to the record, compressed when a range coder is given.
    /// </summary>
    /// <param name="data">The bytes of the body.</param>
    /// <param name="coder">The range coder that compresses the body, or null to write it unchanged.</param>
    /// <returns>The number of bytes written unchanged, or 0 when the body was compressed.</returns>
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

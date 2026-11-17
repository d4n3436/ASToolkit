using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one chunk of a <see cref="ResContainer"/>.
/// </summary>
public readonly struct ResChunk
{
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResChunk"/> structure.
    /// </summary>
    /// <param name="data">The bytes from the chunk's tag to the end of its file.</param>
    public ResChunk(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }
    
    /// <summary>
    /// Gets the four-byte chunk tag.
    /// </summary>
    public ReadOnlyMemory<byte> Tag => _data[..4];

    /// <summary>
    /// Gets the size (body - 8-byte header).
    /// </summary>
    public int Size => MemoryMarshal.Read<int>(_data[4..].Span);
    
    /// <summary>
    /// Gets the header flags.
    /// </summary>
    public ResChunkFlags Flags => MemoryMarshal.Read<ResChunkFlags>(_data[8..].Span);
    
    /// <summary>
    /// Gets the number of bytes the compression of the chunk's records saved, or 0 when they are stored uncompressed.
    /// </summary>
    public int CompressionSavings => HasExtendedHeader ? MemoryMarshal.Read<int>(_data[12..].Span) : 0;
    
    /// <summary>
    /// Gets the number of records this chunk has.
    /// </summary>
    public int Count => HasExtendedHeader ? MemoryMarshal.Read<int>(_data[16..].Span) : (int)Flags;

    /// <summary>
    /// Gets the bytes after the chunk's tag and size.
    /// </summary>
    public ReadOnlyMemory<byte> Body => _data.Slice(8, Size);

    /// <summary>
    /// Returns bytes from the start of the body, running past its end into the rest of the file.
    /// </summary>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The bytes, shorter than the length only at the end of the file.</returns>
    public ReadOnlyMemory<byte> ReadBody(int length) => _data[8..][..Math.Min(length, _data.Length - 8)];

    /// <summary>
    /// Enumerates over the records on this chunk.
    /// </summary>
    public IEnumerable<ReadOnlyMemory<byte>> Records => EnumerateRecords();

    private IEnumerable<ReadOnlyMemory<byte>> EnumerateRecords()
    {
        if (_data.IsEmpty)
            yield break;
        
        int start = HasExtendedHeader ? 20 : 12;
        
        for (int i = 0; i < Count; i++)
        {
            int length = MemoryMarshal.Read<int>(_data[start..].Span);
            yield return _data.Slice(start + 4, length);
            start += 4 + length;
        }
    }

    private bool HasExtendedHeader => Flags.HasFlag(ResChunkFlags.ExtendedHeader);
}
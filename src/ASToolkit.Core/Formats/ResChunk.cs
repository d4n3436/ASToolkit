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
    /// <param name="data">The data.</param>
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
    public int Size => (int)MemoryMarshal.Read<uint>(_data[4..].Span);
    
    /// <summary>
    /// Gets the header flags.
    /// </summary>
    public uint Flags => MemoryMarshal.Read<uint>(_data[8..].Span);
    
    /// <summary>
    /// Gets the header value that follows the flags when bit 31 is set.
    /// </summary>
    public uint Reserved => HasExtraFields ? MemoryMarshal.Read<uint>(_data[12..].Span) : 0;
    
    /// <summary>
    /// Gets the number of records this chunk has.
    /// </summary>
    public uint Count => HasExtraFields ? MemoryMarshal.Read<uint>(_data[16..].Span) : Flags;

    /// <summary>
    /// Enumerates over the records on this chunk.
    /// </summary>
    public IEnumerable<ReadOnlyMemory<byte>> Records => EnumerateRecords();

    private IEnumerable<ReadOnlyMemory<byte>> EnumerateRecords()
    {
        if (_data.IsEmpty)
            yield break;
        
        int start = HasExtraFields ? 20 : 12;
        
        for (uint i = 0; i < Count; i++)
        {
            int length = (int)MemoryMarshal.Read<uint>(_data[start..].Span);
            yield return _data.Slice(start + 4, length);
            start += 4 + length;
        }
    }

    private bool HasExtraFields => (Flags & 0x80000000) != 0;
}
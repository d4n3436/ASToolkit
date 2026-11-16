using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of a RES container, a shared format used in resource files in Alien Shooter games.
/// </summary>
public readonly struct ResContainer
{
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResContainer"/> structure.
    /// </summary>
    /// <param name="data">The data.</param>
    public ResContainer(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <summary>
    /// Gets the four magic bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Magic => _data[..4];

    /// <summary>
    /// Gets the size (file length - 8-byte header).
    /// </summary>
    public int Size => (int)MemoryMarshal.Read<uint>(_data[4..].Span);
    
    /// <summary>
    /// Gets the four-byte kind.
    /// </summary>
    public ReadOnlyMemory<byte> Kind => _data[8..12];

    /// <summary>
    /// Gets the chunks the resource holds.
    /// </summary>
    public IEnumerable<ResChunk> Chunks => EnumerateChunks();

    /// <summary>
    /// Finds the first chunk with a tag, returning false when the container has none.
    /// </summary>
    /// <param name="tag">The four-byte tag.</param>
    /// <param name="found">The chunk.</param>
    public bool TryGetChunk(ReadOnlySpan<byte> tag, out ResChunk found)
    {
        foreach (var chunk in Chunks)
        {
            if (!chunk.Tag.Span.SequenceEqual(tag))
                continue;
            
            found = chunk;
            return true;
        }

        found = default;
        return false;
    }

    private IEnumerable<ResChunk> EnumerateChunks()
    {
        if (_data.IsEmpty)
            yield break;
        
        int end = Math.Min(_data.Length, Size + 8);
        int offset = 12;

        while (offset + 8 < end)
        {
            int size = (int)MemoryMarshal.Read<uint>(_data.Span[(offset + 4)..]);
            int start = offset + 8;
            if (size < 0 || size > _data.Length - start)
            {
                yield break;
            }

            yield return new ResChunk(_data[offset..]);
            offset = start + ((size + 1) & ~1);
        }
    }
}
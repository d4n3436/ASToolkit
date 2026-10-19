using System.Numerics;
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
    public IEnumerable<ResChunkView> Chunks => EnumerateChunks();

    private IEnumerable<ResChunkView> EnumerateChunks()
    {
        if (_data.IsEmpty)
            yield break;
        
        int end = Math.Min(_data.Length, Size + 8);
        int offset = 12;

        while (offset + 8 < end)
        {
            int size = (int)MemoryMarshal.Read<uint>(_data.Span[(offset + 4)..]);
            int start = offset + 8;
            
            yield return new ResChunkView(_data.Slice(offset, size + 8));
            offset = start + ((size + 1) & ~1);
        }
    }
}
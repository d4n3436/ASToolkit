using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a RES container whose chunks and records can change.
/// </summary>
public sealed class ResContainerBuilder
{
    private const int FileHeaderSize = 12;
    private const int ChunkHeaderSize = 8;
    private const uint CountedFlag = 0x80000000;

    /// <summary>
    /// Gets the four magic bytes.
    /// </summary>
    public required byte[] Magic { get; init; }

    /// <summary>
    /// Gets the four kind bytes.
    /// </summary>
    public required byte[] Kind { get; init; }

    /// <summary>
    /// Gets the chunks in file order.
    /// </summary>
    public List<ResChunkBuilder> Chunks { get; } = [];

    /// <summary>
    /// Returns the first chunk with a tag.
    /// </summary>
    public ResChunkBuilder GetChunk(string tag) => Chunks.First(c => c.Tag == tag);

    /// <summary>
    /// Returns the first chunk with a tag, or null when there is none.
    /// </summary>
    public ResChunkBuilder? FindChunk(string tag) => Chunks.FirstOrDefault(c => c.Tag == tag);

    /// <summary>
    /// Creates a builder with a copy of the chunks of a container.
    /// </summary>
    public static ResContainerBuilder Parse(ReadOnlyMemory<byte> data)
    {
        var container = new ResContainer(data);
        if (!container.Magic.Span.SequenceEqual("RES "u8) && !container.Magic.Span.SequenceEqual("RIFF"u8))
        {
            throw new InvalidDataException($"Not a RES file ({Encoding.ASCII.GetString(container.Magic.Span)}).");
        }

        var builder = new ResContainerBuilder { Magic = container.Magic.ToArray(), Kind = container.Kind.ToArray() };
        foreach (var chunk in container.Chunks)
        {
            builder.Chunks.Add(new ResChunkBuilder
            {
                Tag = Encoding.ASCII.GetString(chunk.Tag.Span),
                Flags = chunk.Flags,
                Res = chunk.Reserved,
                Records = chunk.Records.Select(r => r.ToArray()).ToList()
            });
        }

        return builder;
    }

    /// <summary>
    /// Returns the bytes of the container.
    /// </summary>
    public byte[] Build()
    {
        int length = FileHeaderSize;

        // The game's files leave the last chunk unpadded
        for (int index = 0; index < Chunks.Count; index++)
        {
            int size = ChunkHeaderSize + GetBodyLength(Chunks[index]);
            length += index < Chunks.Count - 1 ? GetPaddedLength(size) : size;
        }

        byte[] result = new byte[length];
        Magic.CopyTo(result, 0);
        MemoryMarshal.Write(result.AsSpan(4), (uint)(length - 8));
        Kind.CopyTo(result, 8);
        int position = FileHeaderSize;
        foreach (var chunk in Chunks)
        {
            position += WriteChunk(chunk, result.AsSpan(position));
        }

        return result;
    }

    // The advance includes the pad byte, which the last chunk has no room for
    private static int WriteChunk(ResChunkBuilder chunk, Span<byte> destination)
    {
        bool counted = (chunk.Flags & CountedFlag) != 0;
        Encoding.ASCII.GetBytes(chunk.Tag, destination);
        MemoryMarshal.Write(destination[4..], (uint)GetBodyLength(chunk));
        MemoryMarshal.Write(destination[8..], counted ? chunk.Flags : (uint)chunk.Records.Count);
        int position = ChunkHeaderSize + 4;
        if (counted)
        {
            MemoryMarshal.Write(destination[position..], chunk.Res);
            MemoryMarshal.Write(destination[(position + 4)..], (uint)chunk.Records.Count);
            position += 8;
        }

        foreach (byte[] record in chunk.Records)
        {
            MemoryMarshal.Write(destination[position..], (uint)record.Length);
            record.CopyTo(destination[(position + 4)..]);
            position += 4 + record.Length;
        }

        return GetPaddedLength(position);
    }

    private static int GetBodyLength(ResChunkBuilder chunk)
    {
        int length = (chunk.Flags & CountedFlag) != 0 ? 12 : 4;
        foreach (byte[] record in chunk.Records)
        {
            length += 4 + record.Length;
        }

        return length;
    }

    private static int GetPaddedLength(int length) => (length + 1) & ~1;
}

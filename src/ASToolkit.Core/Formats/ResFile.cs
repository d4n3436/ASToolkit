using System.Buffers.Binary;
using System.Text;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the game's chunk container used by maps and objects.res. A file is "RES " or "RIFF", a u32 size and a
/// four-byte kind, followed by chunks of tag, u32 size and a body padded to even length. A body is a u32 flags word
/// (plus u32 res and u32 count when bit 31 is set) followed by records of u32 length and bytes.
/// </summary>
public sealed class ResFile
{
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
    public List<ResChunk> Chunks { get; } = [];

    /// <summary>
    /// Gets or sets the bytes after the last chunk.
    /// </summary>
    public byte[] Trailer { get; set; } = [];

    /// <summary>
    /// Returns the first chunk with a tag.
    /// </summary>
    public ResChunk GetChunk(string tag) => Chunks.First(c => c.Tag == tag);

    /// <summary>
    /// Returns the first chunk with a tag, or null when there is none.
    /// </summary>
    public ResChunk? FindChunk(string tag) => Chunks.FirstOrDefault(c => c.Tag == tag);

    /// <summary>
    /// Reads a container from a file.
    /// </summary>
    public static ResFile Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Parses a container.
    /// </summary>
    public static ResFile Parse(ReadOnlySpan<byte> data)
    {
        if (!data[..4].SequenceEqual("RES "u8) && !data[..4].SequenceEqual("RIFF"u8))
        {
            throw new InvalidDataException($"Not a RES file ({Encoding.ASCII.GetString(data[..4])}).");
        }

        var file = new ResFile { Magic = data[..4].ToArray(), Kind = data[8..12].ToArray() };
        long total = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        int end = (int)Math.Min(data.Length, total + 8);
        int offset = 12;
        while (offset + 8 <= end)
        {
            string tag = Encoding.ASCII.GetString(data.Slice(offset, 4));
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            int start = offset + 8;
            file.Chunks.Add(ParseChunk(tag, data.Slice(start, Math.Min(size, data.Length - start))));
            offset = start + ((size + 1) & ~1);
        }

        file.Trailer = offset < data.Length ? data[offset..].ToArray() : [];
        return file;
    }

    /// <summary>
    /// Returns the bytes of the container.
    /// </summary>
    public byte[] Build()
    {
        using var stream = new MemoryStream();
        foreach (var chunk in Chunks)
        {
            byte[] body = chunk.IsUnparsed ? chunk.Tail : BuildBody(chunk);
            stream.Write(Encoding.ASCII.GetBytes(chunk.Tag));
            WriteUInt32(stream, (uint)body.Length);
            stream.Write(body);
            if ((body.Length & 1) != 0)
            {
                stream.WriteByte(0);
            }
        }

        int chunksLength = (int)stream.Length;
        stream.Write(Trailer);
        byte[] chunks = stream.ToArray();
        byte[] result = new byte[12 + chunks.Length];
        Magic.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(chunksLength + 4));
        Kind.CopyTo(result, 8);
        chunks.CopyTo(result, 12);
        return result;
    }

    private static ResChunk ParseChunk(string tag, ReadOnlySpan<byte> body)
    {
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(body);
        uint res = 0;
        uint count = flags;
        int position = 4;
        if ((flags & 0x80000000) != 0)
        {
            res = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
            count = BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
            position = 12;
        }

        var records = new List<byte[]>();
        for (uint i = 0; i < count; i++)
        {
            if (position + 4 > body.Length)
            {
                return new ResChunk { Tag = tag, Flags = flags, Res = res, IsUnparsed = true, Tail = body.ToArray() };
            }

            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[position..]);
            if (length < 0 || position + 4 + length > body.Length)
            {
                return new ResChunk { Tag = tag, Flags = flags, Res = res, IsUnparsed = true, Tail = body.ToArray() };
            }

            records.Add(body.Slice(position + 4, length).ToArray());
            position += 4 + length;
        }

        return new ResChunk { Tag = tag, Flags = flags, Res = res, Records = records, Tail = body[position..].ToArray() };
    }

    private static byte[] BuildBody(ResChunk chunk)
    {
        using var body = new MemoryStream();
        bool counted = (chunk.Flags & 0x80000000) != 0;
        WriteUInt32(body, counted ? chunk.Flags : (uint)chunk.Records.Count);
        if (counted)
        {
            WriteUInt32(body, chunk.Res);
            WriteUInt32(body, (uint)chunk.Records.Count);
        }

        foreach (byte[] record in chunk.Records)
        {
            WriteUInt32(body, (uint)record.Length);
            body.Write(record);
        }

        body.Write(chunk.Tail);
        return body.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}

using System.Buffers.Binary;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the placed objects of a .map file.
/// </summary>
public sealed class GameMap
{
    private const int EntrySize = 28;
    private static readonly byte[] EndMarker = [0xff, 0xff, 0xff, 0xff];

    /// <summary>
    /// Gets the placed objects in file order.
    /// </summary>
    public List<MapSprite> Sprites { get; init; } = [];

    /// <summary>
    /// Gets or sets the map width and height in pixels.
    /// </summary>
    public (double Width, double Height) Size { get; set; }

    /// <summary>
    /// Gets or sets the camera position at the start.
    /// </summary>
    public (double X, double Y) Scroll { get; set; }

    /// <summary>
    /// Reads the placed objects of a map. Entries with non-finite or far-off coordinates are skipped.
    /// </summary>
    public static GameMap Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Reads the placed objects of a map from its bytes. Entries with non-finite or far-off coordinates are skipped.
    /// </summary>
    public static GameMap Parse(ReadOnlySpan<byte> data)
    {
        var file = ResFile.Parse(data);
        byte[] head = file.GetChunk("HEAD").Records[0];
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(20));
        var payloads = new Dictionary<uint, byte[]>();
        foreach (byte[] record in file.GetChunk("SPRD").Records)
        {
            if (record.Length >= 4)
            {
                payloads[BinaryPrimitives.ReadUInt32LittleEndian(record)] = record[4..];
            }
        }

        var map = new GameMap
        {
            Size = (BinaryPrimitives.ReadSingleLittleEndian(head), BinaryPrimitives.ReadSingleLittleEndian(head.AsSpan(4))),
            Scroll = (BinaryPrimitives.ReadSingleLittleEndian(head.AsSpan(8)), BinaryPrimitives.ReadSingleLittleEndian(head.AsSpan(12)))
        };
        ReadOnlySpan<byte> entries = file.GetChunk("SPR ").Records[0];
        for (int index = 0; index < (entries.Length - 4) / EntrySize; index++)
        {
            var e = entries.Slice(index * EntrySize, EntrySize);
            uint handle = BinaryPrimitives.ReadUInt32LittleEndian(e);
            double x, y, z;
            if (version >= 12)
            {
                x = BinaryPrimitives.ReadSingleLittleEndian(e[8..]);
                y = BinaryPrimitives.ReadSingleLittleEndian(e[12..]);
                z = BinaryPrimitives.ReadSingleLittleEndian(e[16..]);
            }
            else
            {
                x = BinaryPrimitives.ReadInt32LittleEndian(e[8..]);
                y = BinaryPrimitives.ReadInt32LittleEndian(e[12..]);
                z = BinaryPrimitives.ReadInt32LittleEndian(e[16..]);
            }

            if (!IsSane(x) || !IsSane(y) || !IsSane(z))
            {
                continue;
            }

            map.Sprites.Add(new MapSprite(handle, (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(e[4..]), x, y, z, e[20],
                BinaryPrimitives.ReadUInt32LittleEndian(e[24..]), payloads.GetValueOrDefault(handle) ?? new byte[8]));
        }

        return map;
    }

    /// <summary>
    /// Writes the map, taking every chunk other than the objects and the header from a template map.
    /// </summary>
    public void Write(string path, string templatePath) => File.WriteAllBytes(path, ToBytes(File.ReadAllBytes(templatePath)));

    /// <summary>
    /// Returns the bytes of the map, taking every chunk other than the objects and the header from a template map's bytes.
    /// </summary>
    public byte[] ToBytes(ReadOnlySpan<byte> template)
    {
        var file = ResFile.Parse(template);
        byte[] entries = new byte[Sprites.Count * EntrySize + 4];
        var payloads = new List<byte[]>(Sprites.Count + 1);
        for (int spriteIndex = 0; spriteIndex < Sprites.Count; spriteIndex++)
        {
            var s = Sprites[spriteIndex];
            var e = entries.AsSpan(spriteIndex * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(e, s.Handle);
            BinaryPrimitives.WriteUInt32LittleEndian(e[4..], (uint)s.Id);
            BinaryPrimitives.WriteSingleLittleEndian(e[8..], (float)s.X);
            BinaryPrimitives.WriteSingleLittleEndian(e[12..], (float)s.Y);
            BinaryPrimitives.WriteSingleLittleEndian(e[16..], (float)s.Z);
            e[20] = s.Dir;
            BinaryPrimitives.WriteUInt32LittleEndian(e[24..], s.Army);
            byte[] payload = new byte[4 + s.Payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, s.Handle);
            s.Payload.CopyTo(payload, 4);
            payloads.Add(payload);
        }

        EndMarker.CopyTo(entries, entries.Length - 4);
        payloads.Add(EndMarker);
        file.GetChunk("SPR ").Records = [entries];
        file.GetChunk("SPRD").Records = payloads;
        byte[] head = new byte[24];
        BinaryPrimitives.WriteSingleLittleEndian(head, (float)Size.Width);
        BinaryPrimitives.WriteSingleLittleEndian(head.AsSpan(4), (float)Size.Height);
        BinaryPrimitives.WriteSingleLittleEndian(head.AsSpan(8), (float)Scroll.X);
        BinaryPrimitives.WriteSingleLittleEndian(head.AsSpan(12), (float)Scroll.Y);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(16), 1999);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(20), 12);
        file.GetChunk("HEAD").Records = [head];

        // Drop the template's groups since they reference its sprite handles
        var groups = file.FindChunk("GROU");
        groups?.Records = [EndMarker];

        return file.Build();
    }

    private static bool IsSane(double v) => double.IsFinite(v) && Math.Abs(v) < 1e6;
}

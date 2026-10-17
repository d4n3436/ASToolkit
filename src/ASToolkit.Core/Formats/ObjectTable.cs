using System.Buffers.Binary;
using System.Text;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the entries of objects.res by ID.
/// </summary>
public sealed class ObjectTable
{
    private const int BodySize = 688;
    private readonly Dictionary<KnownObject, GameObject> _objects;

    private ObjectTable(Dictionary<KnownObject, GameObject> objects)
    {
        _objects = objects;
    }

    /// <summary>
    /// Gets every entry.
    /// </summary>
    public IReadOnlyCollection<GameObject> All => _objects.Values;

    /// <summary>
    /// Reads objects.res.
    /// </summary>
    public static ObjectTable Load(string path)
    {
        var objects = new Dictionary<KnownObject, GameObject>();
        foreach (ReadOnlySpan<byte> record in ResFile.Load(path).Chunks[0].Records)
        {
            var id = (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(record);
            int nameEnd = record[4..].IndexOf((byte)0) + 4;
            var body = record.Slice(nameEnd + 1, BodySize);
            var spritePath = record[(nameEnd + 1 + BodySize)..^1];
            objects[id] = new GameObject(id, GameTextEncoding.Cyrillic.GetString(record[4..nameEnd]), Encoding.Latin1.GetString(spritePath),
                BinaryPrimitives.ReadUInt32LittleEndian(body[4..]), BinaryPrimitives.ReadUInt32LittleEndian(body[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(body[16..]), BinaryPrimitives.ReadSingleLittleEndian(body[20..]),
                BinaryPrimitives.ReadSingleLittleEndian(body[24..]), BinaryPrimitives.ReadInt32LittleEndian(body[0x1c..]),
                Math.Max(1, (int)BinaryPrimitives.ReadUInt32LittleEndian(body[112..])), (int)BinaryPrimitives.ReadUInt32LittleEndian(body[116..]))
            {
                Tint = (BinaryPrimitives.ReadInt32LittleEndian(body[0x294..]), BinaryPrimitives.ReadInt32LittleEndian(body[0x298..]), BinaryPrimitives.ReadInt32LittleEndian(body[0x29c..]))
            };
        }

        return new ObjectTable(objects);
    }

    /// <summary>
    /// Returns the entry for an ID, or null when the table has none.
    /// </summary>
    public GameObject? Find(KnownObject id) => _objects.GetValueOrDefault(id);
}

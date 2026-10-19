using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the entries of objects.res by ID.
/// </summary>
public sealed class ObjectTable
{
    private static readonly int BodySize = Unsafe.SizeOf<GameObject>();

    private readonly Dictionary<KnownObject, Entry> _entries;

    private ObjectTable(Dictionary<KnownObject, Entry> entries)
    {
        _entries = entries;
    }

    /// <summary>
    /// Gets the ID of every entry.
    /// </summary>
    public IReadOnlyCollection<KnownObject> Ids => _entries.Keys;

    /// <summary>
    /// Reads objects.res.
    /// </summary>
    public static ObjectTable Load(string path)
    {
        var entries = new Dictionary<KnownObject, Entry>();
        foreach (ReadOnlySpan<byte> record in ResFile.Load(path).Chunks[0].Records)
        {
            var id = (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(record);
            int nameEnd = record[4..].IndexOf((byte)0) + 4;
            var spritePath = record[(nameEnd + 1 + BodySize)..^1];
            entries[id] = new Entry(MemoryMarshal.Read<GameObject>(record[(nameEnd + 1)..]), GameTextEncoding.Cyrillic.GetString(record[4..nameEnd]),
                Encoding.Latin1.GetString(spritePath));
        }

        return new ObjectTable(entries);
    }

    /// <summary>
    /// Finds the entry for an ID, returning false when the table has none.
    /// </summary>
    public bool TryFind(KnownObject id, out GameObject obj)
    {
        bool found = _entries.TryGetValue(id, out var entry);
        obj = entry.Object;
        return found;
    }

    /// <summary>
    /// Returns the Russian name of an entry.
    /// </summary>
    public string GetName(KnownObject id) => _entries[id].Name;

    /// <summary>
    /// Returns the sprite file of an entry.
    /// </summary>
    public string GetPath(KnownObject id) => _entries[id].Path;

    private readonly record struct Entry(GameObject Object, string Name, string Path);
}

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
    private static readonly int OldBodySize = Unsafe.SizeOf<OldGameObject>();

    private readonly Dictionary<KnownObject, Entry> _entries;

    private ObjectTable(Dictionary<KnownObject, Entry> entries, bool isAlienShooter2)
    {
        _entries = entries;
        IsAlienShooter2 = isAlienShooter2;
    }

    /// <summary>
    /// Gets a value indicating whether the table has the longer entries of Alien Shooter 2.
    /// </summary>
    public bool IsAlienShooter2 { get; }

    /// <summary>
    /// Gets the ID of every entry.
    /// </summary>
    public IReadOnlyCollection<KnownObject> Ids => _entries.Keys;

    /// <summary>
    /// Reads objects.res.
    /// </summary>
    public static ObjectTable Load(string path)
    {
        var records = new ResContainer(File.ReadAllBytes(path)).Chunks.First().Records;
        var entries = new Dictionary<KnownObject, Entry>();
        bool anyExtended = false;
        foreach (var memory in records)
        {
            var record = memory.Span;
            var id = (KnownObject)MemoryMarshal.Read<uint>(record);
            int nameEnd = record[4..].IndexOf((byte)0) + 4;
            int bodyStart = nameEnd + 1;

            int pathEnd = record.Length - 1;
            int pathStart = bodyStart + OldBodySize;
            bool isOld = pathStart < pathEnd && IsPath(record[pathStart..pathEnd]);
            GameObject obj;
            if (isOld)
            {
                obj = new GameObject(MemoryMarshal.Read<OldGameObject>(record[bodyStart..pathStart]));
            }
            else
            {
                anyExtended = true;
                pathStart = bodyStart + BodySize;
                obj = MemoryMarshal.Read<GameObject>(record[bodyStart..pathStart]);
            }

            entries[id] = new Entry(obj, GameTextEncoding.Cyrillic.GetString(record[4..nameEnd]),
                Encoding.Latin1.GetString(record[pathStart..pathEnd]));
        }

        return new ObjectTable(entries, anyExtended);
    }

    private static bool IsPath(ReadOnlySpan<byte> path)
    {
        foreach (byte b in path)
        {
            if (b is < 0x20 or > 0x7E)
                return false;
        }

        return true;
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

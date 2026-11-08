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
    private static readonly int AlienShooterBodySize = Unsafe.SizeOf<AlienShooterGameObject>();
    private static readonly int ZombieShooterBodySize = Unsafe.SizeOf<ZombieShooterGameObject>();
    private static readonly int ZombieShooter2BodySize = Unsafe.SizeOf<ZombieShooter2GameObject>();
    private static readonly int[] BodySizes = [AlienShooterBodySize, ZombieShooterBodySize, BodySize, ZombieShooter2BodySize];

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
        int bodySize = FindBodySize(records.First().Span);
        bool anyExtended = bodySize == BodySize || bodySize == ZombieShooter2BodySize;
        var entries = new Dictionary<KnownObject, Entry>();
        foreach (var memory in records)
        {
            var record = memory.Span;
            var id = (KnownObject)MemoryMarshal.Read<uint>(record);
            int nameEnd = record[4..].IndexOf((byte)0) + 4;
            int bodyStart = nameEnd + 1;
            int pathStart = bodyStart + bodySize;
            var body = record[bodyStart..pathStart];

            GameObject obj;
            if (bodySize == AlienShooterBodySize)
            {
                obj = new GameObject(MemoryMarshal.Read<AlienShooterGameObject>(body));
            }
            else if (bodySize == ZombieShooterBodySize)
            {
                obj = new GameObject(MemoryMarshal.Read<ZombieShooterGameObject>(body));
            }
            else if (bodySize == ZombieShooter2BodySize)
            {
                obj = new GameObject(MemoryMarshal.Read<ZombieShooter2GameObject>(body));
            }
            else
            {
                obj = ReadAlienShooter2(body);
            }

            entries[id] = new Entry(obj, GameTextEncoding.Cyrillic.GetString(record[4..nameEnd]),
                Encoding.Latin1.GetString(record[pathStart..^1]));
        }

        return new ObjectTable(entries, anyExtended);
    }

    private static GameObject ReadAlienShooter2(ReadOnlySpan<byte> body)
    {
        var obj = MemoryMarshal.Read<GameObject>(body);
        obj.Opacity = 255;
        return obj;
    }

    private static int FindBodySize(ReadOnlySpan<byte> record)
    {
        int nameEnd = record[4..].IndexOf((byte)0) + 4;
        int bodyStart = nameEnd + 1;
        int pathEnd = record.Length - 1;
        foreach (int size in BodySizes)
        {
            if (bodyStart + size < pathEnd && IsPath(record[(bodyStart + size)..pathEnd]))
            {
                return size;
            }
        }

        return BodySize;
    }

    private static bool IsPath(ReadOnlySpan<byte> path)
    {
        foreach (byte b in path)
        {
            if (!(char.IsAsciiLetterOrDigit((char)b) || b is (byte)'\\' or (byte)'/' or (byte)'.' or (byte)'_' or (byte)'-' or (byte)' ' or (byte)'(' or (byte)')'))
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

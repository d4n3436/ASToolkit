using System.Buffers.Binary;
using System.Numerics;
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
    private static readonly int WeaponSize = Unsafe.SizeOf<WeaponRecord>();
    private static readonly int AlienShooterWeaponSize = Unsafe.SizeOf<AlienShooterWeaponRecord>();
    private static readonly int[] BodySizes = [AlienShooterBodySize, ZombieShooterBodySize, BodySize, ZombieShooter2BodySize];
    private readonly Dictionary<KnownObject, Entry> _entries;
    private readonly WeaponRecord[] _weapons;
    private readonly SfxRecord[] _sounds;

    private ObjectTable(Dictionary<KnownObject, Entry> entries, bool isAlienShooter2, WeaponRecord[] weapons, SfxRecord[] sounds)
    {
        _weapons = weapons;
        _sounds = sounds;
        _entries = entries;
        IsAlienShooter2 = isAlienShooter2;
    }

    /// <summary>
    /// Gets a value indicating whether the table has the longer entries of Alien Shooter 2.
    /// </summary>
    public bool IsAlienShooter2 { get; }

    /// <summary>
    /// Gets the version of the script language the game uses.
    /// </summary>
    public ScriptVersion ScriptVersion => IsAlienShooter2 ? ScriptVersion.AlienShooter2 : ScriptVersion.AlienShooter;

    /// <summary>
    /// Gets the ID of every entry.
    /// </summary>
    public IReadOnlyCollection<KnownObject> Ids => _entries.Keys;

    /// <summary>
    /// Gets the sounds of the SFX chunk, empty for games other than Alien Shooter.
    /// </summary>
    public IReadOnlyList<SfxRecord> Sounds => _sounds;

    internal float Gravitation { get; private init; }

    internal float Gravitation2 { get; private init; }

    internal Vector2 ScrollSpeed { get; private init; }

    internal int HoverDelay { get; private init; }

    internal uint SelectedGamma { get; private init; }

    internal uint HoveredEnemyGamma { get; private init; }

    internal uint HoveredGamma { get; private init; }

    /// <summary>
    /// Reads objects.res.
    /// </summary>
    /// <param name="path">The path of objects.res.</param>
    /// <returns>The table of the entries.</returns>
    public static ObjectTable Load(string path)
    {
        var container = new ResContainer(File.ReadAllBytes(path));
        var records = container.Chunks.First().Records;
        int bodySize = FindBodySize(records.First().Span);
        bool anyExtended = bodySize == BodySize || bodySize == ZombieShooter2BodySize;
        var entries = new Dictionary<KnownObject, Entry>();
        foreach (var memory in records)
        {
            var record = memory.Span;
            var id = (KnownObject)BinaryPrimitives.ReadInt32LittleEndian(record);
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
                obj = MemoryMarshal.Read<GameObject>(body);
            }

            entries[id] = new Entry(obj, GameTextEncoding.Cyrillic.GetString(record[4..nameEnd]),
                Encoding.Latin1.GetString(record[pathStart..^1]));
        }

        var data = container.Chunks.First(chunk => chunk.Tag.Span.SequenceEqual("CNST"u8)).Records.First();
        var constants = MemoryMarshal.Read<ObjectConstants>(data.Span);
        var sounds = bodySize == AlienShooterBodySize ? ReadRecords(container, "SFX "u8).Select(record => new SfxRecord(record)).ToArray() : [];
        return new ObjectTable(entries, anyExtended, ReadWeapons(container), sounds)
        {
            Gravitation = constants.Gravitation,
            Gravitation2 = constants.Gravitation2,
            ScrollSpeed = new Vector2(constants.ScrollSpeedX, constants.ScrollSpeedY),
            HoverDelay = constants.HoverDelay,
            SelectedGamma = constants.SelectedGamma,
            HoveredEnemyGamma = constants.HoveredEnemyGamma,
            HoveredGamma = constants.HoveredGamma
        };
    }

    /// <summary>
    /// Attempts to find the entry with the given ID.
    /// </summary>
    /// <param name="id">The ID of the entry.</param>
    /// <param name="obj">The entry if it was found; otherwise, the default entry.</param>
    /// <returns><see langword="true"/> if the table has an entry with the ID; otherwise, <see langword="false"/>.</returns>
    public bool TryFind(KnownObject id, out GameObject obj)
    {
        bool found = _entries.TryGetValue(id, out var entry);
        obj = entry.Object;
        return found;
    }

    /// <summary>
    /// Returns the name of an entry.
    /// </summary>
    /// <param name="id">The ID of the entry.</param>
    /// <returns>The name of the entry.</returns>
    public string GetName(KnownObject id) => _entries[id].Name;

    /// <summary>
    /// Returns the sprite file of an entry.
    /// </summary>
    /// <param name="id">The ID of the entry.</param>
    /// <returns>The sprite file of the entry.</returns>
    public string GetPath(KnownObject id) => _entries[id].Path;

    /// <summary>
    /// Attempts to get the weapon record an object points at, or the first record for an index past the last.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <param name="weapon">The weapon record if the table has weapon records; otherwise, the default record.</param>
    /// <returns><see langword="true"/> if the table has weapon records; <see langword="false"/> if the table's weapon records use an unknown layout.</returns>
    public bool TryGetWeapon(in GameObject obj, out WeaponRecord weapon)
    {
        weapon = _weapons.Length == 0 ? default : _weapons[obj.WeaponIndex >= 0 && obj.WeaponIndex < _weapons.Length ? obj.WeaponIndex : 0];
        return _weapons.Length > 0;
    }

    private static List<ReadOnlyMemory<byte>> ReadRecords(ResContainer container, ReadOnlySpan<byte> tag)
    {
        var records = new List<ReadOnlyMemory<byte>>();
        foreach (var chunk in container.Chunks)
        {
            if (chunk.Tag.Span.SequenceEqual(tag))
            {
                records.AddRange(chunk.Records);
            }
        }

        return records;
    }

    private static WeaponRecord[] ReadWeapons(ResContainer container)
    {
        foreach (var chunk in container.Chunks)
        {
            if (!chunk.Tag.Span.SequenceEqual("WEAP"u8))
                continue;

            var records = chunk.Records.ToArray();
            if (records.All(record => record.Length == AlienShooterWeaponSize))
                return records.Select(record =>
                {
                    var weapon = MemoryMarshal.Read<AlienShooterWeaponRecord>(record.Span);
                    return new WeaponRecord(weapon);
                }).ToArray();

            return records.All(record => record.Length == WeaponSize) ? records.Select(record => MemoryMarshal.Read<WeaponRecord>(record.Span)).ToArray() : [];
        }

        return [];
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

    private readonly record struct Entry(GameObject Object, string Name, string Path);
}

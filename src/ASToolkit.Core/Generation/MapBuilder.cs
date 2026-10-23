using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the assembly of a map's placed objects.
/// </summary>
public sealed class MapBuilder
{
    private static readonly byte[] PlayerStartPayload = Convert.FromHexString("0000000002000000050100000501000000000000");

    /// <summary>
    /// Represents the payload of a placed monster.
    /// </summary>
    public static readonly byte[] MonsterPayload = Convert.FromHexString("000000000000000003000000");

    private readonly ArmyTable _armies;
    private readonly HashSet<(KnownObject, long, long, long)> _placed = [];
    private uint _handle = 0x10000000;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapBuilder"/> class.
    /// </summary>
    public MapBuilder(ArmyTable armies)
    {
        _armies = armies;
    }

    /// <summary>
    /// Gets the placed objects in order.
    /// </summary>
    public List<MergedMapSprite> Sprites { get; } = [];

    /// <summary>
    /// Places an object and returns its index, or -1 when a unique placement repeats an earlier one.
    /// </summary>
    public int Add(KnownObject id, double x, double y, double z = 0, int dir = 0, ReadOnlyMemory<byte> payload = default, uint? army = null, bool unique = true)
    {
        var key = (id, (long)Math.Round(x), (long)Math.Round(y), (long)Math.Round(z));
        if (!_placed.Add(key) && unique)
        {
            return -1;
        }

        _handle += 0x10;
        byte[] data = !payload.IsEmpty ? payload.ToArray()
            : id == KnownObject.PlayerStart ? PlayerStartPayload
            : KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id) ? MonsterPayload
            : new byte[8];
        Sprites.Add(new MergedMapSprite(_handle, id, new Vector3((float)x, (float)y, (float)z), (byte)(dir & 255), army ?? _armies.GetArmy(id), data));
        return Sprites.Count - 1;
    }

    /// <summary>
    /// Returns a map of the placed objects, sized to hold them.
    /// </summary>
    public MapResourceBuilder ToMap(Vector2 scroll)
    {
        double maxX = Sprites.Max(s => s.Position.X);
        double maxY = Sprites.Max(s => s.Position.Y);
        var map = new MapResourceBuilder
        {
            Sprites = Sprites,
            Size = (Math.Ceiling((maxX + 300) / 100) * 100, Math.Ceiling((maxY + 300) / 100) * 100),
            Scroll = scroll
        };
        return map;
    }
}

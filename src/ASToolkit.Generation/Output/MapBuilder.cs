using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Analysis.Levels;

namespace ASToolkit.Generation.Output;

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
    private readonly List<SpriteSource> _sources = [];
    private int _handle = 0x10000000;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapBuilder"/> class.
    /// </summary>
    /// <param name="armies">The armies of the objects the builder places.</param>
    public MapBuilder(ArmyTable armies)
    {
        _armies = armies;
    }

    /// <summary>
    /// Gets the placed objects in order.
    /// </summary>
    public List<MergedMapSprite> Sprites { get; } = [];

    /// <summary>
    /// Gets the positions of the teleport pads placed so far.
    /// </summary>
    public List<PointF> Pads { get; } = [];

    /// <summary>
    /// Gets or sets the source of the sprites added without one.
    /// </summary>
    public SpriteSource Source { get; set; }

    /// <summary>
    /// Places an object on the map.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="x">The map x.</param>
    /// <param name="y">The map y.</param>
    /// <param name="z">The height.</param>
    /// <param name="dir">The direction of the object.</param>
    /// <param name="payload">The payload bytes of the sprite, or empty for the default payload of the object.</param>
    /// <param name="army">The army of the object, or null for the army of its object ID.</param>
    /// <param name="unique">Whether to skip the object when another one was placed at the same position.</param>
    /// <param name="source">The source of the sprite, or null for <see cref="Source"/>.</param>
    /// <returns>The index of the sprite, or -1 if the placement was skipped.</returns>
    public int Add(KnownObject id, float x, float y, float z = 0, Angle dir = default, ReadOnlyMemory<byte> payload = default, Army? army = null, bool unique = true, SpriteSource? source = null)
    {
        var key = (id, (long)MathF.Round(x), (long)MathF.Round(y), (long)MathF.Round(z));
        if (!_placed.Add(key) && unique)
        {
            return -1;
        }

        _handle += 0x10;
        byte[] data = !payload.IsEmpty ? payload.ToArray()
            : id == KnownObject.PlayerStart ? PlayerStartPayload
            : KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id) ? MonsterPayload
            : new byte[8];
        Sprites.Add(new MergedMapSprite(_handle, id, new Vector3(x, y, z), dir, army ?? _armies.GetArmy(id), data));
        _sources.Add(source ?? Source);
        if (id is KnownObject.TeleportPad or KnownObject.SilentTeleportPad)
        {
            Pads.Add(new PointF(x, y));
        }

        return Sprites.Count - 1;
    }

    /// <summary>
    /// Returns the number of sprites from each source.
    /// </summary>
    /// <returns>The number of sprites by source.</returns>
    public IReadOnlyDictionary<SpriteSource, int> GetProvenance() => _sources.CountBy(source => source).ToDictionary();

    /// <summary>
    /// Creates a map of the placed objects, sized to hold them and lit by the given ambient light or the template's.
    /// </summary>
    /// <param name="scroll">The initial scroll position of the map.</param>
    /// <param name="ambient">The ambient light, or null to take the one of the template.</param>
    /// <returns>The map.</returns>
    public MapResourceBuilder ToMap(Vector2 scroll, Gamma? ambient = null)
    {
        float maxX = Sprites.Max(s => s.Position.X);
        float maxY = Sprites.Max(s => s.Position.Y);
        var map = new MapResourceBuilder
        {
            Sprites = Sprites,
            Size = new SizeF(MathF.Ceiling((maxX + 300) / 100) * 100, MathF.Ceiling((maxY + 300) / 100) * 100),
            Scroll = scroll,
            Ambient = ambient
        };
        return map;
    }
}

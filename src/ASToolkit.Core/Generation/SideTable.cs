using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the side field the original levels give each object, 1 for monsters and generators, 2 for breakable things and
/// 0 for the rest.
/// </summary>
public sealed class SideTable
{
    private readonly Dictionary<KnownObject, uint> _sides;

    private SideTable(Dictionary<KnownObject, uint> sides)
    {
        _sides = sides;
    }

    /// <summary>
    /// Reads the most common side of every object ID from the given original maps.
    /// </summary>
    public static SideTable Load(string gameFolder, IEnumerable<string> levels)
    {
        var counts = new Dictionary<KnownObject, List<(uint Side, int Count)>>();
        foreach (string level in levels)
        {
            foreach (var sprite in MapResourceBuilder.Load(Path.Combine(gameFolder, "Maps", level)).Sprites)
            {
                if (!counts.TryGetValue(sprite.Id, out var list))
                {
                    list = [];
                    counts[sprite.Id] = list;
                }

                int i = list.FindIndex(t => t.Side == sprite.Side);
                if (i < 0)
                {
                    list.Add((sprite.Side, 1));
                }
                else
                {
                    list[i] = (sprite.Side, list[i].Count + 1);
                }
            }
        }

        return new SideTable(counts.ToDictionary(p => p.Key, p => p.Value.OrderByDescending(t => t.Count).First().Side));
    }

    /// <summary>
    /// Returns the side for an object ID.
    /// </summary>
    public uint GetSide(KnownObject id) => _sides.TryGetValue(id, out uint side) ? side : KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id) ? 1u : 0u;
}

using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the army field the original levels give each object, 1 for monsters and generators, 2 for breakable things and
/// 0 for the rest.
/// </summary>
public sealed class ArmyTable
{
    private readonly Dictionary<KnownObject, uint> _armies;

    private ArmyTable(Dictionary<KnownObject, uint> armies)
    {
        _armies = armies;
    }

    /// <summary>
    /// Reads the most common army of every object ID from the given original maps.
    /// </summary>
    public static ArmyTable Load(string gameFolder, IEnumerable<string> levels)
    {
        var counts = new Dictionary<KnownObject, List<(uint Army, int Count)>>();
        foreach (string level in levels)
        {
            foreach (var sprite in MapResourceBuilder.Load(Path.Combine(gameFolder, "Maps", level)).Sprites)
            {
                if (!counts.TryGetValue(sprite.Id, out var list))
                {
                    list = [];
                    counts[sprite.Id] = list;
                }

                int i = list.FindIndex(t => t.Army == sprite.Army);
                if (i < 0)
                {
                    list.Add((sprite.Army, 1));
                }
                else
                {
                    list[i] = (sprite.Army, list[i].Count + 1);
                }
            }
        }

        return new ArmyTable(counts.ToDictionary(p => p.Key, p => p.Value.OrderByDescending(t => t.Count).First().Army));
    }

    /// <summary>
    /// Returns the army for an object ID.
    /// </summary>
    public uint GetArmy(KnownObject id) => _armies.TryGetValue(id, out uint army) ? army : KnownObject.Monsters.Contains(id) || KnownObject.Bosses.Contains(id) ? 1u : 0u;
}

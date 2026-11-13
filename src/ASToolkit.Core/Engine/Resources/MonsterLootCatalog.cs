using System.Collections.Frozen;
using System.Text.RegularExpressions;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed partial class MonsterLootCatalog
{
    private readonly FrozenDictionary<KnownObject, LootTable> _birth;
    private readonly FrozenSet<KnownObject> _death;

    public MonsterLootCatalog(string gameFolder)
    {
        var birth = new Dictionary<KnownObject, LootTable>();
        var death = new HashSet<KnownObject>();
        string? tablePath = GamePath.Resolve(gameFolder, Path.Combine("Maps", "compaign_birth.lgc"));
        string? scriptPath = GamePath.Resolve(gameFolder, Path.Combine("Maps", "MONSTERS.LGC"));
        if (tablePath is not null && scriptPath is not null)
        {
            var table = GameTable.Load(tablePath);
            string script = GameTextEncoding.Cyrillic.GetString(File.ReadAllBytes(scriptPath));
            var groups = new Dictionary<int, LootTable>();
            foreach (Match match in BirthHandler().Matches(script))
            {
                var id = (KnownObject)int.Parse(match.Groups[1].ValueSpan);
                int group = int.Parse(match.Groups[2].ValueSpan);
                if (!groups.TryGetValue(group, out var values))
                {
                    var items = table.GetArray($"BirthItems{group}").Select(item => (KnownObject)item).ToArray();
                    int[] weights = table.GetArray($"BirthItemsP{group}").ToArray();
                    values = new LootTable(items, weights, weights.Sum());
                    groups.Add(group, values);
                }

                birth[id] = values;
            }

            foreach (Match match in DeathHandler().Matches(script))
            {
                death.Add((KnownObject)int.Parse(match.Groups[1].ValueSpan));
            }
        }

        _birth = birth.ToFrozenDictionary();
        _death = death.ToFrozenSet();
    }

    public void AssignItem(Unit unit, Random random)
    {
        if (unit.DropItem != 0 || !_birth.TryGetValue(unit.Kind.Id, out var table) || table.Total == 0)
            return;

        int roll = random.Next(table.Total + 1);
        for (int index = 0; index < table.Items.Length; index++)
        {
            roll -= table.Weights[index];
            if (roll >= 0)
                continue;

            unit.DropItem = table.Items[index];
            return;
        }
    }

    public bool DropsItem(KnownObject id) => _death.Contains(id);

    [GeneratedRegex(@"F(\d+)_14\s*\(int\s+unit\)\s*\{\s*CreateItemsForBirth(\d+)\(unit\);\s*\}")]
    private static partial Regex BirthHandler();

    [GeneratedRegex(@"F(\d+)_1[56]\s*\(int\s+unit\)\s*\{\s*CreateItems\(unit\);\s*\}")]
    private static partial Regex DeathHandler();

    private sealed record LootTable(KnownObject[] Items, int[] Weights, int Total);
}

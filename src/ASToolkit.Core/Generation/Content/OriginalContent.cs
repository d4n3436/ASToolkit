using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents the gameplay content of one original level.
/// </summary>
public sealed class OriginalContent
{
    /// <summary>Gets the level name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the clusters of placed monsters as the designer laid them out.</summary>
    public required IReadOnlyList<IReadOnlyList<GroupMember>> PlacedGroups { get; init; }

    /// <summary>Gets the monster list of every spawner that builds any.</summary>
    public required IReadOnlyList<IReadOnlyList<KnownObject>> SpawnerLists { get; init; }

    /// <summary>Gets the pickups on the floor, by ID.</summary>
    public required IReadOnlyDictionary<KnownObject, int> Pickups { get; init; }

    /// <summary>Gets the pickups inside crates, by ID.</summary>
    public required IReadOnlyDictionary<KnownObject, int> CrateLoot { get; init; }

    /// <summary>Gets the placed bosses, by ID.</summary>
    public required IReadOnlyDictionary<KnownObject, int> Bosses { get; init; }

    /// <summary>Gets the total hit points of every monster.</summary>
    public required int HitPoints { get; init; }

    /// <summary>Gets the share of the hit points spawners bring.</summary>
    public required double SpawnedShare { get; init; }

    /// <summary>Gets the number of mounted cannons.</summary>
    public required int MountedCannons { get; init; }

    /// <summary>Gets the number of fence generators.</summary>
    public required int Generators { get; init; }

    /// <summary>
    /// Reads the content of an original level.
    /// </summary>
    public static OriginalContent Load(string gameFolder, string level, ObjectTable objects)
    {
        var sp = GameMap.Load(Path.Combine(gameFolder, "Maps", level)).Sprites;
        var pickups = new Dictionary<KnownObject, int>();
        var loot = new Dictionary<KnownObject, int>();
        var bosses = new Dictionary<KnownObject, int>();
        var spawners = new List<IReadOnlyList<KnownObject>>();
        int placedHp = 0;
        int spawnedHp = 0;
        foreach (var sprite in sp)
        {
            if (sprite.Id.IsPickup)
            {
                pickups[sprite.Id] = pickups.GetValueOrDefault(sprite.Id) + 1;
            }
            else if (sprite.Id == KnownObject.Crate)
            {
                var item = KnownObject.GetCrateItem(sprite.Payload);
                if (item > 0)
                {
                    loot[item] = loot.GetValueOrDefault(item) + 1;
                }
            }

            if (KnownObject.Bosses.Contains(sprite.Id))
            {
                bosses[sprite.Id] = bosses.GetValueOrDefault(sprite.Id) + 1;
            }

            if (KnownObject.Monsters.Contains(sprite.Id) || KnownObject.Bosses.Contains(sprite.Id))
            {
                placedHp += objects.Find(sprite.Id)?.HitPoints ?? 0;
            }

            var program = sprite.Id == KnownObject.Spawner ? SpawnerProgram.Parse(sprite.Payload) : null;
            if (program is not null)
            {
                var monsters = program.Monsters.ToArray();
                if (monsters.Length > 0)
                {
                    spawners.Add(monsters);
                    spawnedHp += monsters.Sum(m => objects.Find(m)?.HitPoints ?? 0);
                }
            }
        }

        return new OriginalContent
        {
            Name = level,
            PlacedGroups = ClusterMonsters(sp),
            SpawnerLists = spawners,
            Pickups = pickups,
            CrateLoot = loot,
            Bosses = bosses,
            HitPoints = placedHp + spawnedHp,
            SpawnedShare = spawnedHp / (double)Math.Max(1, placedHp + spawnedHp),
            MountedCannons = sp.Count(s => s.Id == KnownObject.MountedCannon),
            Generators = sp.Count(s => s.Id == KnownObject.Generator)
        };
    }

    /// <summary>
    /// Returns the props within 170 pixels of each breaker of the given levels.
    /// </summary>
    public static IReadOnlyList<PropPlacement>[] LoadBreakerScenes(string gameFolder, IEnumerable<string> levels)
    {
        var scenes = new List<IReadOnlyList<PropPlacement>>();
        foreach (string level in levels)
        {
            var sp = GameMap.Load(Path.Combine(gameFolder, "Maps", level)).Sprites;
            foreach (var breaker in sp.Where(s => s.Id == KnownObject.Breaker))
            {
                scenes.Add(sp.Where(o => o.Id is KnownObject.ObjectMarker or KnownObject.Shelves or KnownObject.Table or KnownObject.Cabinet or KnownObject.Transformer or KnownObject.SoldierCorpse or KnownObject.BloodPool or KnownObject.LargeBloodPool && Math.Sqrt(Math.Pow(o.X - breaker.X, 2) + Math.Pow(o.Y - breaker.Y, 2)) < 170)
                    .Select(o => new PropPlacement(o.Id, o.X - breaker.X, o.Y - breaker.Y, o.Z, o.Dir)).ToArray());
            }
        }

        return scenes.ToArray();
    }

    /// <summary>
    /// Returns every dynamite site of the given levels with the pads and generators within 130 pixels of it.
    /// </summary>
    public static SiteTemplate[] LoadDynamiteSites(string gameFolder, IEnumerable<string> levels, LevelTheme theme)
    {
        var sites = new List<SiteTemplate>();
        foreach (string level in levels)
        {
            var sp = GameMap.Load(Path.Combine(gameFolder, "Maps", level)).Sprites;
            foreach (var sprite in sp.Where(s => s.Id == KnownObject.DynamiteSite))
            {
                var members = sp
                    .Where(o => (o.Id is KnownObject.TeleportPad or KnownObject.BigMonsterHole or KnownObject.SmallMonsterHole || o.Id.IsEndlessGenerator) && Math.Sqrt(Math.Pow(o.X - sprite.X, 2) + Math.Pow(o.Y - sprite.Y, 2)) < 130)
                    .Select(o => new SiteMember(o.Id, o.X - sprite.X, o.Y - sprite.Y, o.Z, o.Dir, o.Army,
                        o.Payload.Length <= 12 ? o.Payload : new byte[o.Id == KnownObject.TeleportPad ? 12 : 8]))
                    .ToList();
                if (theme == LevelTheme.Lab)
                {
                    members.RemoveAll(m => m.Id is KnownObject.BigMonsterHole or KnownObject.SmallMonsterHole);
                }

                if (theme == LevelTheme.Military && members.All(m => m.Id != KnownObject.TeleportPad))
                {
                    continue;
                }

                int rank = members.Where(m => m.Id >= KnownObject.FirstEndlessGenerator).Select(m => m.Id - KnownObject.FirstEndlessGenerator).DefaultIfEmpty(0).Max();
                sites.Add(new SiteTemplate(sprite.Z, sprite.Dir, rank, members));
            }
        }

        return sites.ToArray();
    }

    private static IReadOnlyList<GroupMember>[] ClusterMonsters(List<MapSprite> sp)
    {
        var monsters = sp.Where(s => KnownObject.Monsters.Contains(s.Id) || s.Id is KnownObject.Bat or KnownObject.Diver).ToArray();
        bool[] seen = new bool[monsters.Length];
        var groups = new List<IReadOnlyList<GroupMember>>();
        for (int monsterIndex = 0; monsterIndex < monsters.Length; monsterIndex++)
        {
            if (seen[monsterIndex])
            {
                continue;
            }

            var group = new List<int> { monsterIndex };
            seen[monsterIndex] = true;
            for (int memberIndex = 0; memberIndex < group.Count; memberIndex++)
            {
                var a = monsters[group[memberIndex]];
                for (int innerMonsterIndex = 0; innerMonsterIndex < monsters.Length; innerMonsterIndex++)
                {
                    if (!seen[innerMonsterIndex] && Math.Sqrt(Math.Pow(monsters[innerMonsterIndex].X - a.X, 2) + Math.Pow(monsters[innerMonsterIndex].Y - a.Y, 2)) < 110)
                    {
                        seen[innerMonsterIndex] = true;
                        group.Add(innerMonsterIndex);
                    }
                }
            }

            double cx = group.Average(k => monsters[k].X);
            double cy = group.Average(k => monsters[k].Y);
            groups.Add(group.Select(k => new GroupMember(monsters[k].Id, monsters[k].X - cx, monsters[k].Y - cy, monsters[k].Dir)).ToArray());
        }

        return groups.ToArray();
    }
}

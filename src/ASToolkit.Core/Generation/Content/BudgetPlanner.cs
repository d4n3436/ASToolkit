using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Provides the content budget of a generated level, drawn from original levels of similar difficulty.
/// </summary>
public static class BudgetPlanner
{
    private const float ItemJitter = 0.1f;
    private const float DensitySlack = 1.5f;
    private static readonly HashSet<KnownObject> CampaignBosses = [KnownObject.Boss1, KnownObject.Boss2, KnownObject.Boss3, KnownObject.Boss4, KnownObject.TankBoss, KnownObject.RedTankWeaponBoss, KnownObject.PowerfulBoss3, KnownObject.DiverBoss, KnownObject.FinalMonster];
    private static readonly HashSet<KnownObject> DroppedFromGroups = [KnownObject.Boss1, KnownObject.Boss4, KnownObject.PowerfulBoss3, KnownObject.RedTankWeaponBoss, KnownObject.TankBoss, KnownObject.Boss2];

    /// <summary>
    /// Plans the budget of a level.
    /// </summary>
    public static LevelBudget Plan(Random rng, int level, LevelTarget target, OriginalContentSet content, ObjectTable objects)
    {
        float hpTarget = target.HitPoints * (0.9f + 0.2f * rng.NextSingle());
        var campaign = content.GetLevel(ReferenceLevelSet.GetCampaignName(level));
        var pickupSource = content.GetLevel(target.ContentLevel);
        var sources = content.Levels
            .Where(l => l.HitPoints >= hpTarget / 3 && l.HitPoints <= hpTarget * 3)
            .ToArray();
        
        if (sources.Length < 2)
        {
            sources = content.Levels
                .Where(l => l.HitPoints > 0)
                .OrderBy(l => Math.Abs(MathF.Log(l.HitPoints / hpTarget)))
                .Take(3)
                .ToArray();
        }

        int batCap = 2 * (level - 1);
        int bats = 0;

        var spawnerPool = sources
            .SelectMany(s => s.SpawnerLists.Select(list => (Source: s, List: list)))
            .ToArray();
        
        var smallSpawners = spawnerPool
            .Where(p => GetHitPoints(p.List) <= Math.Max(hpTarget * 0.22f, 1))
            .ToArray();
        
        var spawners = smallSpawners.Length > 0 ? smallSpawners : spawnerPool;
        float density = GetDensity(campaign.SpawnerLists.SelectMany(list => list).ToArray());
        var alike = spawners.Where(p => GetDensity(p.List) <= density * DensitySlack && GetDensity(p.List) >= density / DensitySlack).ToArray();
        if (density > 0 && alike.Length > 0)
        {
            spawners = alike;
        }
        
        var groupPool = sources
            .SelectMany(s => s.PlacedGroups.Select(g => (Source: s, Group: g)))
            .ToArray();
        
        var smallGroups = groupPool
            .Where(p => GetHitPoints(p.Group.Select(m => m.Id)) <= Math.Max(hpTarget * 0.12f, 1))
            .ToArray();
        
        var groups = smallGroups.Length > 0 ? smallGroups : groupPool;
        rng.Shuffle(spawners);
        rng.Shuffle(groups);

        float ambushChance = Math.Clamp(campaign.SpawnedShare, 0.5f, 0.95f);
        var ambushes = new List<IReadOnlyList<KnownObject>>();
        var placed = new List<IReadOnlyList<GroupMember>>();
        int ambushHp = 0;
        int placedHp = 0;
        int nextSpawner = 0;
        int nextGroup = 0;
        for (int index = 0; ambushHp + placedHp < hpTarget && index < 5000; index++)
        {
            bool ambush = spawners.Length > 0 && (groups.Length == 0 || (ambushHp < campaign.SpawnedShare * hpTarget && rng.NextSingle() < ambushChance));
            int hp;
            if (ambush)
            {
                var (_, list) = spawners[nextSpawner++ % spawners.Length];
                var cleaned = Clean(list);
                if (cleaned.Count == 0)
                {
                    continue;
                }

                ambushes.Add(cleaned);
                hp = GetHitPoints(cleaned);
                ambushHp += hp;
            }
            else
            {
                var (_, group) = groups[nextGroup++ % groups.Length];
                var cleaned = CleanGroup(group);
                if (cleaned.Count == 0)
                {
                    continue;
                }

                placed.Add(cleaned);
                hp = GetHitPoints(cleaned.Select(m => m.Id));
                placedHp += hp;
            }

        }

        ambushes.AddRange(campaign.SpawnerLists.Where(list => list.Any(CampaignBosses.Contains)));

        // Open the level with the smallest placed group when the draws gave it none
        var smallest = placed.Count != 0 ? null : groups
                .Select(g => CleanGroup(g.Group))
                .Where(g => g.Count > 0)
                .MinBy(g => GetHitPoints(g.Select(m => m.Id)));
        
        if (smallest is not null)
        {
            placed.Add(smallest);
            placedHp += GetHitPoints(smallest.Select(m => m.Id));
        }

        // Build plain monsters in place of endless generators
        ambushes = ambushes.Select(IReadOnlyList<KnownObject> (list) => list.Select(id => id.IsEndlessGenerator ? KnownObject.Monster1 : id).ToList()).ToList();
        var items = new Dictionary<KnownObject, int>();
        foreach ((var item, int count) in pickupSource.Pickups.Concat(pickupSource.CrateLoot).OrderBy(p => p.Key))
        {
            if (item == KnownObject.Dynamite || item.IsWeapon)
            {
                continue;
            }

            float n = count * (1 - ItemJitter + 2 * ItemJitter * rng.NextSingle());
            int whole = (int)n + (rng.NextSingle() < n - (int)n ? 1 : 0);
            for (int thirdIndex = 0; thirdIndex < whole; thirdIndex++)
            {
                var replaced = ItemSchedule.Replace(item, level, rng);
                items[replaced] = items.GetValueOrDefault(replaced) + 1;
            }
        }

        foreach (var weapon in ItemSchedule.GetWeapons(level))
        {
            var ammo = ItemSchedule.GetAmmo(weapon);
            items[ammo] = Math.Max(2, items.GetValueOrDefault(ammo));
        }

        if (target.DynamiteSites > 0)
        {
            items[KnownObject.Dynamite] = target.DynamiteSites;
        }

        var weapons = pickupSource.Pickups
            .Concat(pickupSource.CrateLoot)
            .Where(p => p.Key.IsWeapon)
            .OrderBy(p => p.Key)
            .SelectMany(p => Enumerable.Repeat(p.Key, p.Value))
            .ToArray();

        return new LevelBudget
        {
            TargetHitPoints = (int)hpTarget,
            PlacedGroups = placed,
            Ambushes = ambushes,
            Bosses = campaign.Bosses.Where(p => CampaignBosses.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value),
            Items = items,
            Weapons = weapons.ToList(),
            DynamiteSites = target.DynamiteSites,
            MountedCannons = campaign.MountedCannons,
            Generators = campaign.Generators,
            PlacedHitPoints = placedHp,
            AmbushHitPoints = ambushHp
        };

        List<GroupMember> CleanGroup(IEnumerable<GroupMember> group)
        {
            var cleaned = new List<GroupMember>();
            foreach (var member in group)
            {
                var id = Clean([member.Id]);
                if (id.Count == 1)
                {
                    cleaned.Add(member with { Id = id[0] });
                }
            }

            return cleaned;
        }

        List<KnownObject> Clean(IEnumerable<KnownObject> monsters)
        {
            var kept = new List<KnownObject>();
            foreach (var monster in monsters)
            {
                var id = monster == KnownObject.Diver ? KnownObject.RedTankWeaponMonster : monster;
                if (id == KnownObject.DiverBoss || DroppedFromGroups.Contains(id) || (id == KnownObject.Bat && bats >= batCap))
                {
                    continue;
                }

                if (id == KnownObject.Bat)
                {
                    bats++;
                }

                kept.Add(id);
            }

            return kept;
        }

        int GetHitPoints(IEnumerable<KnownObject> monsters) => monsters.Sum(m => objects.TryFind(m, out var obj) ? obj.MaxHp : 0);

        // Count the monsters for every hit point they bring
        float GetDensity(IReadOnlyList<KnownObject> monsters) => monsters.Count / (float)Math.Max(1, GetHitPoints(monsters));
    }
}

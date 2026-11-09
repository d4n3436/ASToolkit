using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class CombatKindTable
{
    private const int MaxSpawnDepth = 2;
    private const int MaxLaunchDepth = 3;

    private readonly string _gameFolder;
    private readonly ObjectTable _objects;
    private readonly Dictionary<KnownObject, CombatKind?> _kinds = [];
    private readonly Dictionary<KnownObject, int> _attackDamage = [];
    private readonly Dictionary<string, int> _spriteSpeeds = new(StringComparer.OrdinalIgnoreCase);

    public CombatKindTable(string gameFolder, ObjectTable objects)
    {
        _gameFolder = gameFolder;
        _objects = objects;
    }

    public CombatKind? Find(KnownObject id)
    {
        if (_kinds.TryGetValue(id, out var known))
        {
            return known;
        }

        CombatKind? kind = null;
        if (id > 0 && _objects.TryFind(id, out var obj))
        {
            _objects.TryGetWeapon(obj, out var weapon);
            kind = new CombatKind(id, obj, weapon, GetSpriteSpeed(id))
            {
                DeathSpawns = FindSpawns(obj, SpriteAnimation.Death),
                Death2Spawns = FindSpawns(obj, SpriteAnimation.Death2),
            };
        }

        _kinds[id] = kind;
        return kind;
    }

    // Sums the damage one attack carries down its fight children, or a projectile's death child
    public int GetAttackDamage(CombatKind creator)
    {
        if (_attackDamage.TryGetValue(creator.Id, out int damage))
            return damage;
        
        damage = MeasureDamage(creator, 0);
        _attackDamage[creator.Id] = damage;

        return damage;
    }

    private int MeasureDamage(CombatKind creator, int depth)
    {
        var child = Find(creator.FightChild);
        if (child is null || depth >= MaxLaunchDepth)
        {
            return 0;
        }

        if (child.Damage > 0)
        {
            return creator.FightCount * child.Damage;
        }

        if (child.FightChild != 0)
        {
            return creator.FightCount * MeasureDamage(child, depth + 1);
        }

        var payload = Find(child.DeathChild);
        return payload is null ? 0 : creator.FightCount * payload.Damage;
    }

    // Collects the units a death state creates, directly or through the object it leaves
    private KnownObject[] FindSpawns(GameObject obj, SpriteAnimation animation)
    {
        var spawns = new List<KnownObject>();
        var child = (KnownObject)obj.Children[(int)animation];
        int count = Math.Max(1, obj.ChildCounts[(int)animation]);
        for (int depth = 0; depth < MaxSpawnDepth && child > 0 && _objects.TryFind(child, out var created); depth++)
        {
            if (IsUnit(created))
            {
                spawns.AddRange(Enumerable.Repeat(child, count));
                break;
            }

            child = (KnownObject)created.Children[(int)SpriteAnimation.Death];
        }

        return spawns.ToArray();
    }

    private int GetSpriteSpeed(KnownObject id)
    {
        string path = _objects.GetPath(id);
        if (_spriteSpeeds.TryGetValue(path, out int speed))
            return speed;
        
        string? resolved = GamePath.Resolve(_gameFolder, path);
        speed = resolved is not null && resolved.EndsWith(".vid", StringComparison.OrdinalIgnoreCase) ? new VidResource(File.ReadAllBytes(resolved)).Speed : 0;
        _spriteSpeeds[path] = speed;

        return speed;
    }

    private bool IsUnit(in GameObject obj) => obj is { MaxHp: > 0, IsHittable: true } && _objects.TryGetWeapon(obj, out var weapon) && weapon.BattleRange > 0;
}

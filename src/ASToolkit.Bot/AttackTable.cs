using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

internal sealed class AttackTable
{
    private readonly SpriteCatalog _definitions;
    private readonly Dictionary<KnownObject, AttackProfile> _profiles = [];

    public AttackTable(SpriteCatalog definitions)
    {
        _definitions = definitions;
    }

    public AttackProfile Get(SpriteDefinition creator)
    {
        if (!_profiles.TryGetValue(creator.Id, out var profile))
        {
            profile = Measure(creator, 0);
            _profiles.Add(creator.Id, profile);
        }

        return profile;
    }

    private AttackProfile Measure(SpriteDefinition creator, int depth)
    {
        if (depth >= SpriteCatalog.MaxLaunchDepth)
            return default;

        var child = _definitions.Find((KnownObject)Math.Abs((int)creator.FightChild));

        if (child is null)
            return default;

        if (child.Damage > 0)
            return new AttackProfile(creator.FightCount * child.Damage, child.BlastRadius, !child.ShouldSkipOwnArmy, Rounds: creator.FightCount, BlastHeight: GetBlastHeight(child));

        if (child.FightChild != 0)
        {
            var profile = Measure(child, depth + 1);

            return profile with { Damage = creator.FightCount * profile.Damage, Rounds = creator.FightCount * profile.Rounds };
        }

        var payload = _definitions.Find(child.DeathChild);

        if (payload is not null)
            return new AttackProfile(creator.FightCount * payload.Damage, payload.BlastRadius, !payload.ShouldSkipOwnArmy, Rounds: creator.FightCount, BlastHeight: GetBlastHeight(payload));

        var carried = _definitions.Find(child.Object.Link);

        return carried is null ? default : new AttackProfile(creator.FightCount * carried.Damage, carried.BlastRadius, !carried.ShouldSkipOwnArmy,
            child.Speed * child.GetAnimationDuration(child.InitialAnimation), creator.FightCount, GetBlastHeight(carried));
    }

    private static float GetBlastHeight(SpriteDefinition payload) => Math.Max(WeaponSystem.MinBlastHeight, payload.Object.Size.Z);
}

internal readonly record struct AttackProfile(int Damage, float BlastRadius, bool HurtsOwnArmy, float Reach = float.MaxValue, int Rounds = 1, float BlastHeight = float.MaxValue);

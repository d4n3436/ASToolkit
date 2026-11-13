using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class AttackTable
{
    private readonly ObjectCatalog _kinds;
    private readonly Dictionary<KnownObject, AttackProfile> _profiles = [];

    public AttackTable(ObjectCatalog kinds)
    {
        _kinds = kinds;
    }

    public AttackProfile Get(ObjectDefinition creator)
    {
        if (!_profiles.TryGetValue(creator.Id, out var profile))
        {
            profile = Measure(creator, 0);
            _profiles.Add(creator.Id, profile);
        }

        return profile;
    }

    private AttackProfile Measure(ObjectDefinition creator, int depth)
    {
        if (depth >= ObjectCatalog.MaxLaunchDepth)
            return default;

        var child = _kinds.Find((KnownObject)Math.Abs((int)creator.FightChild));

        if (child is null)
            return default;

        if (child.Damage > 0)
            return new AttackProfile(creator.FightCount * child.Damage, child.BlastRadius, !child.SkipsOwnArmy);

        if (child.FightChild != 0)
        {
            var profile = Measure(child, depth + 1);

            return profile with { Damage = creator.FightCount * profile.Damage };
        }

        var payload = _kinds.Find(child.DeathChild);

        if (payload is not null)
            return new AttackProfile(creator.FightCount * payload.Damage, payload.BlastRadius, !payload.SkipsOwnArmy);

        var carried = _kinds.Find(child.Object.Link);

        return carried is null ? default : new AttackProfile(creator.FightCount * carried.Damage, carried.BlastRadius, !carried.SkipsOwnArmy,
            child.Speed * child.GetAnimationDuration(child.InitialAnimation));
    }
}

internal readonly record struct AttackProfile(int Damage, float BlastRadius, bool HurtsOwnArmy, float Reach = float.MaxValue);

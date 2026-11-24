using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

internal sealed class AttackTable
{
    private readonly Map _map;
    private readonly Dictionary<KnownObject, AttackProfile> _profiles = [];

    public AttackTable(Map map)
    {
        _map = map;
    }

    public AttackProfile Get(Vid creator)
    {
        if (!_profiles.TryGetValue(creator.Id, out var profile))
        {
            profile = Measure(creator, 0);
            _profiles.Add(creator.Id, profile);
        }

        return profile;
    }

    private AttackProfile Measure(Vid creator, int depth)
    {
        if (depth >= Map.MaxLaunchDepth)
            return default;

        var child = FindChild((KnownObject)Math.Abs((int)creator.FightChild));

        if (child is null)
            return default;

        if (child.Damage > 0)
            return new AttackProfile(creator.FightCount * child.Damage, child.DeathRange, !child.ShouldSkipOwnArmy, Rounds: creator.FightCount, BlastHeight: GetBlastHeight(child));

        if (child.FightChild != 0)
        {
            var profile = Measure(child, depth + 1);

            return profile with { Damage = creator.FightCount * profile.Damage, Rounds = creator.FightCount * profile.Rounds };
        }

        var payload = FindChild(child.DeathChild);

        if (payload is not null)
            return new AttackProfile(creator.FightCount * payload.Damage, payload.DeathRange, !payload.ShouldSkipOwnArmy, Rounds: creator.FightCount, BlastHeight: GetBlastHeight(payload));

        var carried = FindChild(child.Link);

        return carried is null ? default : new AttackProfile(creator.FightCount * carried.Damage, carried.DeathRange, !carried.ShouldSkipOwnArmy,
            child.Speed * child.GetAnimationDuration(child.InitialAnimation), creator.FightCount, GetBlastHeight(carried));
    }

    private Vid? FindChild(KnownObject id) => id == KnownObject.None ? null : _map.FindVid(id);

    private static float GetBlastHeight(Vid payload) => Math.Max(Map.MinBlastHeight, payload.Size.Z);
}

internal readonly record struct AttackProfile(int Damage, float BlastRadius, bool CanHurtOwnArmy, float Reach = float.MaxValue, int Rounds = 1, float BlastHeight = float.MaxValue);

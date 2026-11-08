using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents a fight planned for a level, such as its room, its monsters, the rooms they or their spawners may stand in, and an ambush's trigger.
/// </summary>
/// <param name="Order">The walk order where the fight starts.</param>
/// <param name="Region">The plan region the fight belongs to.</param>
/// <param name="HitPoints">The hit points of the fight's monsters.</param>
/// <param name="Groups">The fight's monsters, one group for a standing fight and one for each spawner of an ambush.</param>
/// <param name="Rooms">The plan regions the fight's monsters or spawners may stand in, in order.</param>
/// <param name="Trigger">The trigger of an ambush, or null for a standing fight.</param>
/// <param name="Closet">The spawn closet that releases the ambush, or null.</param>
public sealed record PlannedFight(int Order, int Region, int HitPoints, IReadOnlyList<IReadOnlyList<KnownObject>> Groups, IReadOnlyList<int> Rooms, FightTrigger? Trigger,
    int? Closet)
{
    /// <summary>
    /// Gets the monsters of the fight.
    /// </summary>
    public IReadOnlyList<KnownObject> Monsters => Groups.SelectMany(group => group).ToArray();

    /// <summary>
    /// Gets a value indicating whether spawners release the fight.
    /// </summary>
    public bool IsAmbush => Trigger is not null;
}

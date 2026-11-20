using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the outcome of a playthrough, such as whether the player survived, the time, the kills and the equipment left, with the script's event times and an optional demo.
/// </summary>
/// <param name="StartingLoadout">The equipment carried into the level after shopping.</param>
/// <param name="Survived">Whether the player met the level's victory condition alive.</param>
/// <param name="Time">The time the playthrough took in milliseconds.</param>
/// <param name="Progress">The share of the tour the player walked.</param>
/// <param name="Kills">The number of monsters killed.</param>
/// <param name="Respawns">The number of extra lives used during the level.</param>
/// <param name="MinHealth">The lowest health the player had.</param>
/// <param name="Money">The money the player picked up.</param>
/// <param name="Implants">The implants the player carries at the end.</param>
/// <param name="Pickups">The pickups the player took, in order.</param>
/// <param name="Items">The retained utility items at the end of the level.</param>
/// <param name="Ammo">The ammunition left of each weapon the player holds at the end besides the pistols, keyed by the weapon pickup.</param>
/// <param name="Script">The times of the level script's events in the playthrough.</param>
/// <param name="Demo">The demo the run recorded, or null when the run didn't keep a demo.</param>
public sealed record PlaythroughResult(
    PlayerLoadout? StartingLoadout,
    bool Survived,
    int Time,
    float Progress,
    int Kills,
    int Respawns,
    int MinHealth,
    int Money,
    PlayerImplants Implants,
    IReadOnlyList<KnownObject> Pickups,
    IReadOnlyDictionary<KnownObject, int> Items,
    IReadOnlyDictionary<KnownObject, int> Ammo,
    ScriptTimeline Script,
    DemoResource? Demo);

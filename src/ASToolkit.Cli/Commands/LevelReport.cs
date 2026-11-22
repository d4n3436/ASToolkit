using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Cli.Commands;

internal sealed record LevelReport(
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
    ScriptTimeline Script)
{
    public static LevelReport FromPlaythrough(PlaythroughResult result) => new(result.StartingLoadout, result.Survived, result.Time, result.Progress, result.Kills,
        result.Respawns, result.MinHealth, result.Money, result.Implants, result.Pickups, result.Items, result.Ammo, result.Script);
}

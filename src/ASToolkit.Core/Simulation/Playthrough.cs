using ASToolkit.Core.Formats;
using ASToolkit.Core.Routes;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides a tick simulation of a player bot playing a level with the game's combat rules.
/// </summary>
public sealed class Playthrough
{
    private const int MaxAttempts = 3;

    private readonly CombatKindTable _kinds;
    private readonly ShopBot _shop;
    private readonly IReadOnlyList<int> _ammoBatches;

    /// <summary>
    /// Initializes a new instance of the <see cref="Playthrough"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder, holding the sprites whose speed sets the animation times.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="table">The common table of the game, holding the ammunition batch of each weapon.</param>
    public Playthrough(string gameFolder, ObjectTable objects, GameTable table)
    {
        _kinds = new CombatKindTable(gameFolder, objects);
        _shop = new ShopBot(objects, table);
        _ammoBatches = table.GetArray("GettingAmmo");
    }

    /// <summary>
    /// Plays a level along its tour and returns the outcome.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">What the player carries into the level.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="records">A value indicating whether the result keeps the frames and shots for a replay.</param>
    public PlaythroughResult Run(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool records = false) =>
        new PlaythroughRun(analysis, _kinds, _ammoBatches, loadout, new Random(seed), records).Play();

    /// <summary>
    /// Plays the levels of a campaign in order, shopping before every level but the first with the money picked up and paid at each level's end,
    /// retrying a lost level with the next seeds and stopping at a level lost on every try.
    /// </summary>
    /// <param name="levels">The route analyses of the levels in campaign order.</param>
    /// <param name="endMoney">The money paid at the end of each level.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    public IReadOnlyList<PlaythroughResult> RunCampaign(IReadOnlyList<RouteAnalysis> levels, IReadOnlyList<int> endMoney, int seed)
    {
        var results = new List<PlaythroughResult>();
        int money = 0;
        IReadOnlyDictionary<KnownObject, int> ammo = new Dictionary<KnownObject, int>();
        var implants = default(PlayerImplants);
        for (int level = 0; level < levels.Count; level++)
        {
            int left = money;
            var loadout = level == 0 ? new PlayerLoadout(0, ammo, implants) : _shop.Shop(ref left, ammo, implants);
            var result = Run(levels[level], loadout, seed + level * MaxAttempts);
            for (int attempt = 1; attempt < MaxAttempts && !result.Survived; attempt++)
            {
                result = Run(levels[level], loadout, seed + level * MaxAttempts + attempt);
            }

            results.Add(result);
            if (!result.Survived)
            {
                break;
            }

            money = left + result.Money + (level < endMoney.Count ? endMoney[level] : 0);
            ammo = result.Ammo;
            implants = result.Implants;
        }

        return results;
    }
}

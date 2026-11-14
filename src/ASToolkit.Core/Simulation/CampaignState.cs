using System.Collections.Frozen;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the money and equipment carried between campaign levels.
/// </summary>
/// <param name="Money">The money available before shopping.</param>
/// <param name="Loadout">The equipment retained from the previous level.</param>
public sealed record CampaignState(int Money, PlayerLoadout Loadout)
{
    /// <summary>
    /// The money available when a new campaign starts.
    /// </summary>
    public const int StartingMoney = 20;

    /// <summary>
    /// The extra lives available when a new campaign starts.
    /// </summary>
    public const int StartingLives = 2;

    /// <summary>
    /// Creates the state of a new campaign, or of one started at a later level.
    /// </summary>
    /// <param name="money">The money available before shopping.</param>
    /// <returns>The campaign state holding the money and the starting lives.</returns>
    public static CampaignState CreateInitial(int money = StartingMoney) => new(money,
        new PlayerLoadout(0, FrozenDictionary<KnownObject, int>.Empty, default)
        { Items = new Dictionary<KnownObject, int> { [KnownObject.ExtraLife] = StartingLives }.ToFrozenDictionary() });
}

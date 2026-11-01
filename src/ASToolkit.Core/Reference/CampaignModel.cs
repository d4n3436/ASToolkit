using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the curves and shares fitted to a style's original campaigns that generated campaigns are drawn from, such as hit points, floor
/// and end money by level, gate styles per theme, weapon introductions by price and pickups per floor area.
/// </summary>
public sealed class CampaignModel
{
    /// <summary>
    /// Gets the number of levels of a campaign.
    /// </summary>
    public required int LevelCount { get; init; }

    /// <summary>
    /// Gets the money a cash pickup holds.
    /// </summary>
    public required int CashValue { get; init; }

    /// <summary>
    /// Gets the natural logarithm of the hit points of a level's monsters by level.
    /// </summary>
    public required FittedValue<LinearFit> HitPoints { get; init; }

    /// <summary>
    /// Gets the log-odds of the share of a level's hit points in spawned monsters by level.
    /// </summary>
    public required FittedValue<LinearFit> SpawnedShare { get; init; }

    /// <summary>
    /// Gets the natural logarithm of a level's floor area in square pixels by level.
    /// </summary>
    public required FittedValue<LinearFit> FloorPixels { get; init; }

    /// <summary>
    /// Gets the natural logarithm of the money the game pays at the end of a level by the natural logarithm of the level.
    /// </summary>
    public required FittedValue<LinearFit> EndMoney { get; init; }

    /// <summary>
    /// Gets the share of the levels after the first with lab floor, all before the military ones.
    /// </summary>
    public required FittedValue<float> LabShare { get; init; }

    /// <summary>
    /// Gets the gate styles of the levels of each theme in the order the campaigns introduce them.
    /// </summary>
    public required IReadOnlyDictionary<LevelTheme, FittedValue<IReadOnlyList<GateShare>>> Gates { get; init; }

    /// <summary>
    /// Gets the weapon pickups from the cheapest in the shop to the dearest.
    /// </summary>
    public required IReadOnlyList<KnownObject> Weapons { get; init; }

    /// <summary>
    /// Gets the shop price of each weapon.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, int> WeaponPrices { get; init; }

    /// <summary>
    /// Gets the level a weapon first lies on by its place in the price order, from 1.
    /// </summary>
    public required FittedValue<LinearFit> WeaponLevels { get; init; }

    /// <summary>
    /// Gets the least share of a new weapon's price the money paid and picked up before its level reaches.
    /// </summary>
    public required FittedValue<float> Affordability { get; init; }

    /// <summary>
    /// Gets the number of a level's pickups of weapons that earlier levels hold by level.
    /// </summary>
    public required FittedValue<LinearFit> RepeatedWeapons { get; init; }

    /// <summary>
    /// Gets the pickups of each category per million square pixels of floor by level.
    /// </summary>
    public required IReadOnlyDictionary<ItemCategory, FittedValue<LinearFit>> PickupRates { get; init; }

    /// <summary>
    /// Gets the share of each health, armor, implant and bonus pickup in its category by level.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, FittedValue<LinearFit>> PickupShares { get; init; }

    /// <summary>
    /// Gets the share of the ammunition of each weapon by age, first the weapon that comes next, then the newest and on to the oldest.
    /// </summary>
    public required FittedValue<IReadOnlyList<float>> AmmoAges { get; init; }

    /// <summary>
    /// Gets the secret closets of a level of lab walls per million square pixels of floor by level.
    /// </summary>
    public required FittedValue<LinearFit> SecretClosets { get; init; }

    /// <summary>
    /// Gets the originals after the first level that generated levels take what no model covers yet from.
    /// </summary>
    public required IReadOnlyList<DonorCandidate> Donors { get; init; }

    /// <summary>
    /// Gets the originals a generated first level is copied from, kept when left out of the fit.
    /// </summary>
    public required IReadOnlyList<DonorCandidate> FirstLevels { get; init; }
}

using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents the threat, size, pickups and money of one level of a campaign.
/// </summary>
public sealed record CampaignMeasurement
{
    /// <summary>
    /// Gets the number of the level in its campaign, from 1.
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Gets the map name of the level.
    /// </summary>
    public required string Map { get; init; }

    /// <summary>
    /// Gets the theme of the level's floor.
    /// </summary>
    public required LevelTheme Theme { get; init; }

    /// <summary>
    /// Gets the gate style of the level.
    /// </summary>
    public required LevelStyle GateStyle { get; init; }

    /// <summary>
    /// Gets the hit points of the standing and spawned monsters of the level.
    /// </summary>
    public required int HitPoints { get; init; }

    /// <summary>
    /// Gets the share of the level's hit points in spawned monsters.
    /// </summary>
    public required float SpawnedShare { get; init; }

    /// <summary>
    /// Gets the monster and boss kinds of the level.
    /// </summary>
    public required IReadOnlyList<KnownObject> MonsterKinds { get; init; }

    /// <summary>
    /// Gets the monster kinds no earlier level of the campaign holds.
    /// </summary>
    public required IReadOnlyList<KnownObject> NewMonsterKinds { get; init; }

    /// <summary>
    /// Gets the floor area of the level's places in square pixels.
    /// </summary>
    public required int FloorPixels { get; init; }

    /// <summary>
    /// Gets the length of the level's tour in pixels.
    /// </summary>
    public required int TourPixels { get; init; }

    /// <summary>
    /// Gets the number of sections of the level.
    /// </summary>
    public required int Sections { get; init; }

    /// <summary>
    /// Gets the pickups of the level by category, crates included.
    /// </summary>
    public required IReadOnlyDictionary<ItemCategory, int> Pickups { get; init; }

    /// <summary>
    /// Gets the pickups of the level by object, crates included.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, int> Items { get; init; }

    /// <summary>
    /// Gets the money the level's cash pickups hold.
    /// </summary>
    public required int Money { get; init; }

    /// <summary>
    /// Gets the weapon pickups of the level.
    /// </summary>
    public required IReadOnlyList<KnownObject> Weapons { get; init; }

    /// <summary>
    /// Gets the weapon pickups no earlier level of the campaign holds.
    /// </summary>
    public required IReadOnlyList<KnownObject> NewWeapons { get; init; }

    /// <summary>
    /// Gets the number of new weapons whose first pickup the tour reaches before the first pickup of their ammunition.
    /// </summary>
    public required int NewWeaponsBeforeAmmo { get; init; }

    /// <summary>
    /// Gets the money the game pays at the end of the level, or 0 for the last level of the campaign.
    /// </summary>
    public required int EndMoney { get; init; }
}

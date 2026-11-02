using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Generation.Campaign;

/// <summary>
/// Represents the targets a generated level is built to, drawn by the campaign planner, such as its theme, gate style, monster hit points,
/// weapons, pickups and end-of-level money, and the original level it borrows what no model covers yet from.
/// </summary>
public sealed record LevelBrief
{
    /// <summary>
    /// Gets the number of the level in its campaign, from 1.
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Gets the theme of the level.
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
    /// Gets the floor area of the level in square pixels.
    /// </summary>
    public required int FloorPixels { get; init; }

    /// <summary>
    /// Gets the rooms of the level.
    /// </summary>
    public required int Rooms { get; init; }

    /// <summary>
    /// Gets the number of secret closets of the level.
    /// </summary>
    public required int SecretClosets { get; init; }

    /// <summary>
    /// Gets the weapons the campaign has brought by the level, its own new ones included.
    /// </summary>
    public required IReadOnlyList<KnownObject> Arsenal { get; init; }

    /// <summary>
    /// Gets the weapons no earlier level of the campaign holds.
    /// </summary>
    public required IReadOnlyList<KnownObject> NewWeapons { get; init; }

    /// <summary>
    /// Gets the weapon pickups of the level.
    /// </summary>
    public required IReadOnlyList<KnownObject> Weapons { get; init; }

    /// <summary>
    /// Gets the health, armor, ammunition, cash, implant and bonus pickups of the level.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, int> Pickups { get; init; }

    /// <summary>
    /// Gets the money the game pays at the end of the level.
    /// </summary>
    public required int EndMoney { get; init; }

    /// <summary>
    /// Gets the map name of the original level that the parts of the generator without a model of their own still read.
    /// </summary>
    public required string DonorLevel { get; init; }
}

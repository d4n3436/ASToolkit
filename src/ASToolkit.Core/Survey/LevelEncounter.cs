using System.Drawing;

namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a fight of a level.
/// </summary>
public sealed class LevelEncounter
{
    /// <summary>
    /// Gets the encounter number, from 1 in route order.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets how its monsters come.
    /// </summary>
    public required EncounterKind Kind { get; init; }

    /// <summary>
    /// Gets the place its monsters stand in or come from.
    /// </summary>
    public required int Place { get; init; }

    /// <summary>
    /// Gets how far along the route, in map pixels, it starts.
    /// </summary>
    public required int Route { get; init; }

    /// <summary>
    /// Gets the number of monsters.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>
    /// Gets the hit points of all its monsters together.
    /// </summary>
    public required int HitPoints { get; init; }

    /// <summary>
    /// Gets the monsters by ID, most numerous first.
    /// </summary>
    public required IReadOnlyList<MonsterCount> Monsters { get; init; }

    /// <summary>
    /// Gets how its monsters are laid out.
    /// </summary>
    public required EncounterFormation Formation { get; init; }

    /// <summary>
    /// Gets where in its place it lies, for placed groups and bosses.
    /// </summary>
    public PlaceSpot? Spot { get; init; }

    /// <summary>
    /// Gets where an ambush's monsters come from.
    /// </summary>
    public SpawnSource? Source { get; init; }

    /// <summary>
    /// Gets the number of spawners of an ambush.
    /// </summary>
    public int Spawners { get; init; }

    /// <summary>
    /// Gets the place of the trigger.
    /// </summary>
    public int? TriggerPlace { get; init; }

    /// <summary>
    /// Gets the trigger radius in map pixels.
    /// </summary>
    public int? TriggerRadius { get; init; }

    /// <summary>
    /// Gets the walk from the trigger to the nearest source, in map pixels.
    /// </summary>
    public int? TriggerDistance { get; init; }

    /// <summary>
    /// Gets the key objective near the trigger, or null.
    /// </summary>
    public LandmarkKind? NearKey { get; init; }

    /// <summary>
    /// Gets the place of the dynamite site that shuts it, or null.
    /// </summary>
    public int? ShutBy { get; init; }

    /// <summary>
    /// Gets the center in map pixels.
    /// </summary>
    public required Point Position { get; init; }
}

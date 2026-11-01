namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents the numbers that compare one level with another.
/// </summary>
public sealed class LevelSignature
{
    /// <summary>
    /// Gets the walk of the whole route in map pixels.
    /// </summary>
    public required int TourPixels { get; init; }

    /// <summary>
    /// Gets the route's walk without its returns, in map pixels.
    /// </summary>
    public required int SpinePixels { get; init; }

    /// <summary>
    /// Gets the share of the route that walks ground it walked before.
    /// </summary>
    public required float RevisitShare { get; init; }

    /// <summary>
    /// Gets the floor of the places in square map pixels.
    /// </summary>
    public required int FloorPixels { get; init; }

    /// <summary>
    /// Gets the share of the floor's bounding box that is floor.
    /// </summary>
    public required float BoxFill { get; init; }

    /// <summary>
    /// Gets the number of places.
    /// </summary>
    public required int Places { get; init; }

    /// <summary>
    /// Gets the number of places the route enters.
    /// </summary>
    public required int RoutePlaces { get; init; }

    /// <summary>
    /// Gets the number of independent loops among the walkable connections.
    /// </summary>
    public required int Loops { get; init; }

    /// <summary>
    /// Gets the share of the floor in places the route never enters.
    /// </summary>
    public required float OffRouteShare { get; init; }

    /// <summary>
    /// Gets the floor of the start place in square map pixels.
    /// </summary>
    public required int StartPixels { get; init; }

    /// <summary>
    /// Gets the hit points of every monster.
    /// </summary>
    public required int HitPoints { get; init; }

    /// <summary>
    /// Gets the share of the hit points that spawners build.
    /// </summary>
    public required float SpawnedShare { get; init; }

    /// <summary>
    /// Gets the hit points of the fights in each fifth of the route.
    /// </summary>
    public required IReadOnlyList<int> HitPointsByFifth { get; init; }

    /// <summary>
    /// Gets the route share where the first fight starts.
    /// </summary>
    public float? FirstFight { get; init; }

    /// <summary>
    /// Gets the share of the floor that solid objects block.
    /// </summary>
    public required float BlockedShare { get; init; }

    /// <summary>
    /// Gets the number of breakable objects.
    /// </summary>
    public required int Breakables { get; init; }

    /// <summary>
    /// Gets the share of the floor behind the first gate, or 0 for no gate.
    /// </summary>
    public required float GatedShare { get; init; }

    /// <summary>
    /// Gets the walk from the key to the nearest gate.
    /// </summary>
    public int? KeyToGate { get; init; }

    /// <summary>
    /// Gets the walk with the gate shut, from the start or the blackout to the breaker, or null when the level has none.
    /// </summary>
    public int? KeyWalk { get; init; }

    /// <summary>
    /// Gets the share of the route walked before the first generator, or null when the level has none.
    /// </summary>
    public float? FirstGenerator { get; init; }

    /// <summary>
    /// Gets the share of the items more than 150 map pixels off the route.
    /// </summary>
    public required float OffRouteItemShare { get; init; }
}

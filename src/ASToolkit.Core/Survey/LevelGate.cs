
namespace ASToolkit.Core.Survey;

/// <summary>
/// Represents a gate of a level and its key.
/// </summary>
public sealed class LevelGate
{
    /// <summary>
    /// Gets the gate style.
    /// </summary>
    public required LevelStyle Kind { get; init; }

    /// <summary>
    /// Gets the share of the floor behind the gate.
    /// </summary>
    public required float GatedShare { get; init; }

    /// <summary>
    /// Gets the place of the key, the breaker or generator.
    /// </summary>
    public int? KeyPlace { get; init; }

    /// <summary>
    /// Gets how far along the route, in map pixels, the route reaches the key.
    /// </summary>
    public int? KeyRoute { get; init; }

    /// <summary>
    /// Gets the walk from the key to the nearest gate it opens, in map pixels.
    /// </summary>
    public int? KeyToGate { get; init; }

    /// <summary>
    /// Gets the open places next to the gate.
    /// </summary>
    public required IReadOnlyList<int> GatePlaces { get; init; }

    /// <summary>
    /// Gets the number of door or fence connections that shut.
    /// </summary>
    public required int Connections { get; init; }

    /// <summary>
    /// Gets the place of the blackout trigger.
    /// </summary>
    public int? TriggerPlace { get; init; }

    /// <summary>
    /// Gets how far along the route, in map pixels, the lights go out.
    /// </summary>
    public int? TriggerRoute { get; init; }
}

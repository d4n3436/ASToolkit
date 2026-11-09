using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the outcome and the record of a playthrough.
/// </summary>
public sealed class PlaythroughResult
{
    /// <summary>
    /// Gets a value indicating whether the player reached the end of the tour alive.
    /// </summary>
    public required bool Survived { get; init; }

    /// <summary>
    /// Gets the time the playthrough took in milliseconds.
    /// </summary>
    public required int Time { get; init; }

    /// <summary>
    /// Gets the share of the tour the player walked.
    /// </summary>
    public required float Progress { get; init; }

    /// <summary>
    /// Gets the number of monsters killed.
    /// </summary>
    public required int Kills { get; init; }

    /// <summary>
    /// Gets the lowest health the player had.
    /// </summary>
    public required int MinHealth { get; init; }

    /// <summary>
    /// Gets the state of the player each twentieth of the tour.
    /// </summary>
    public required IReadOnlyList<PlaythroughSample> Samples { get; init; }

    /// <summary>
    /// Gets the frames of the playthrough at a fixed interval, or none when the run kept no record.
    /// </summary>
    public required IReadOnlyList<PlaythroughFrame> Frames { get; init; }

    /// <summary>
    /// Gets the money the player picked up.
    /// </summary>
    public required int Money { get; init; }

    /// <summary>
    /// Gets the implants the player carries at the end.
    /// </summary>
    public required PlayerImplants Implants { get; init; }

    /// <summary>
    /// Gets the pickups the player took, in order.
    /// </summary>
    public required IReadOnlyList<KnownObject> Pickups { get; init; }

    /// <summary>
    /// Gets the ammunition left of each weapon the player holds at the end besides the pistols, keyed by the weapon pickup.
    /// </summary>
    public required IReadOnlyDictionary<KnownObject, int> Ammo { get; init; }

    /// <summary>
    /// Gets the shots fired during the playthrough, or none when the run kept no record.
    /// </summary>
    public required IReadOnlyList<ShotTrace> Shots { get; init; }
}

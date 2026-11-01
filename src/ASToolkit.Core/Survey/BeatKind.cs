namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies what happens to the player at a beat of a level.
/// </summary>
public enum BeatKind
{
    /// <summary>
    /// A fight against standing monsters.
    /// </summary>
    Fight,

    /// <summary>
    /// A fight against spawned monsters.
    /// </summary>
    Ambush,

    /// <summary>
    /// A fight against a boss.
    /// </summary>
    Boss,

    /// <summary>
    /// A breaker, generator or dynamite pickup that opens the way on.
    /// </summary>
    Key,

    /// <summary>
    /// A locked, dark or fenced boundary into the next section.
    /// </summary>
    Lock,

    /// <summary>
    /// Pickups lying in one place.
    /// </summary>
    Reward,

    /// <summary>
    /// Pickups in a secret closet.
    /// </summary>
    Stash,

    /// <summary>
    /// A weapon pickup.
    /// </summary>
    Weapon,

    /// <summary>
    /// A checkpoint.
    /// </summary>
    Checkpoint,

    /// <summary>
    /// A mounted cannon or the trigger of a blackout.
    /// </summary>
    SetPiece,

    /// <summary>
    /// A dynamite site, the exit trigger or the end of the tour.
    /// </summary>
    Goal
}

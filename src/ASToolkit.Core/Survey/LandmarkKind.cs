namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies a point of a level that matters to play.
/// </summary>
public enum LandmarkKind
{
    /// <summary>
    /// The player start.
    /// </summary>
    PlayerStart,

    /// <summary>
    /// The breaker.
    /// </summary>
    Breaker,

    /// <summary>
    /// The blackout trigger of the level script.
    /// </summary>
    BlackoutTrigger,

    /// <summary>
    /// A checkpoint.
    /// </summary>
    Checkpoint,

    /// <summary>
    /// A generator that holds the fences up.
    /// </summary>
    Generator,

    /// <summary>
    /// A batch of dynamite charges.
    /// </summary>
    DynamiteCharges,

    /// <summary>
    /// A dynamite site.
    /// </summary>
    DynamiteSite,

    /// <summary>
    /// A boss.
    /// </summary>
    Boss,

    /// <summary>
    /// A mounted cannon.
    /// </summary>
    MountedCannon,

    /// <summary>
    /// The exit trigger of the level script.
    /// </summary>
    ExitTrigger,

    /// <summary>
    /// The last objective of the route.
    /// </summary>
    RouteEnd
}

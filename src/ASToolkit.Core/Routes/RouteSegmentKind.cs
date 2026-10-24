namespace ASToolkit.Core.Routes;

/// <summary>
/// Specifies how the player covers a stretch of a walked route.
/// </summary>
public enum RouteSegmentKind
{
    /// <summary>
    /// A stretch the player walks.
    /// </summary>
    Walked,

    /// <summary>
    /// A stretch a unit the player controls walks while the player stays behind.
    /// </summary>
    Controlled,

    /// <summary>
    /// A jump of a teleport or a lift.
    /// </summary>
    Teleport,

    /// <summary>
    /// A straight line to a place the walk could not reach.
    /// </summary>
    Unreached
}

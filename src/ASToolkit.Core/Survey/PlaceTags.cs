namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the facts that hold for a place.
/// </summary>
[Flags]
public enum PlaceTags
{
    /// <summary>
    /// No fact applies to the place.
    /// </summary>
    None = 0,

    /// <summary>
    /// The place the player starts in.
    /// </summary>
    Start = 1 << 0,

    /// <summary>
    /// A place with one walkable neighbor.
    /// </summary>
    DeadEnd = 1 << 1,

    /// <summary>
    /// A place with four or more walkable neighbors.
    /// </summary>
    Hub = 1 << 2,

    /// <summary>
    /// A place whose removal splits the walkable places.
    /// </summary>
    ChokePoint = 1 << 3,

    /// <summary>
    /// A place on a loop of walkable connections.
    /// </summary>
    OnLoop = 1 << 4,

    /// <summary>
    /// A place the route never enters.
    /// </summary>
    OffRoute = 1 << 5,

    /// <summary>
    /// A place with a jammed door that leads nowhere.
    /// </summary>
    FakeDoor = 1 << 6,

    /// <summary>
    /// The place of the route's last objective.
    /// </summary>
    RouteEnd = 1 << 7
}

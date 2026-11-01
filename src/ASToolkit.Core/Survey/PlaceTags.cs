namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the facts that hold for a place.
/// </summary>
[Flags]
public enum PlaceTags
{
    /// <summary>
    /// No fact.
    /// </summary>
    None = 0,

    /// <summary>
    /// The place the player starts in.
    /// </summary>
    Start = 1,

    /// <summary>
    /// A place with one walkable neighbor.
    /// </summary>
    DeadEnd = 2,

    /// <summary>
    /// A place with four or more walkable neighbors.
    /// </summary>
    Hub = 4,

    /// <summary>
    /// A place whose removal splits the walkable places.
    /// </summary>
    ChokePoint = 8,

    /// <summary>
    /// A place on a loop of walkable connections.
    /// </summary>
    OnLoop = 16,

    /// <summary>
    /// A place the route never enters.
    /// </summary>
    OffRoute = 32,

    /// <summary>
    /// A place with a jammed door that leads nowhere.
    /// </summary>
    FakeDoor = 64,

    /// <summary>
    /// The place of the route's last objective.
    /// </summary>
    RouteEnd = 128
}

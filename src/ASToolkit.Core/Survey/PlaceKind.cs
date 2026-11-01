namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the shape of a place.
/// </summary>
public enum PlaceKind
{
    /// <summary>A room, about as wide as it is long.</summary>
    Room,

    /// <summary>A long room, at least 2.5 times as long as it is wide.</summary>
    Hall,

    /// <summary>A long and narrow passage.</summary>
    Corridor,

    /// <summary>A small place behind a breakable wall.</summary>
    SecretCloset,

    /// <summary>A small place behind a spawn wall or monster hole that monsters come out of.</summary>
    SpawnCloset,

    /// <summary>A small leftover area, such as a doorway niche.</summary>
    Nook,

    /// <summary>A large open area with an irregular outline.</summary>
    Yard
}

namespace ASToolkit.Core.Rules;

/// <summary>
/// Defines how a level gates its route.
/// </summary>
public enum LevelStyle
{
    /// <summary>The doors are locked from the start until the breaker is pulled.</summary>
    LockedDoors,

    /// <summary>The lights go out and the doors lock at a trigger, until the breaker is pulled.</summary>
    Blackout,

    /// <summary>Electric fences close part of the base until a generator is destroyed.</summary>
    Fenced,

    /// <summary>Nothing gates the route.</summary>
    Ungated
}

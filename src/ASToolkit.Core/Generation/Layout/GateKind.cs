namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Defines how a layout gates its route.
/// </summary>
public enum GateKind
{
    /// <summary>The doors are locked from the start until the breaker is pulled.</summary>
    LockedDoors,

    /// <summary>The lights go out at a trigger and the doors lock until the breaker is pulled.</summary>
    Blackout,

    /// <summary>Electric fences close part of the base until a generator is destroyed.</summary>
    Fences
}

namespace ASToolkit.Core.Routes;

/// <summary>
/// Defines the gate state a stretch of the tour is walked under.
/// </summary>
public enum RoutePhase
{
    /// <summary>No gate is up.</summary>
    Free,

    /// <summary>The doors are locked.</summary>
    DoorsLocked,

    /// <summary>The fences are up.</summary>
    FencesUp
}

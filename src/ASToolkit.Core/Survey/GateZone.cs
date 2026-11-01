namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies the part of a level's gating a place belongs to.
/// </summary>
public enum GateZone
{
    /// <summary>The floor reachable from the start before any gate opens.</summary>
    Open,

    /// <summary>The floor behind doors locked until the breaker is pulled.</summary>
    Locked,

    /// <summary>The floor shut off by the doors that lock at the blackout.</summary>
    Blackout,

    /// <summary>The floor behind electric fences.</summary>
    Fenced,

    /// <summary>The floor behind a breakable wall, a spawn wall or a monster hole.</summary>
    Secret
}

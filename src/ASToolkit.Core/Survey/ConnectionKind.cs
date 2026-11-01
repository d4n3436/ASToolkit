namespace ASToolkit.Core.Survey;

/// <summary>
/// Specifies how two places connect.
/// </summary>
public enum ConnectionKind
{
    /// <summary>A gap in a wall line with no frame.</summary>
    Opening,

    /// <summary>A door frame without a door, or a door that stays raised.</summary>
    Doorway,

    /// <summary>A working door.</summary>
    Door,

    /// <summary>A working door that the script locks until the breaker, or at the blackout.</summary>
    LockedDoor,

    /// <summary>A closed or jammed embedded door that never opens.</summary>
    JammedDoor,

    /// <summary>A breakable wall.</summary>
    SecretWall,

    /// <summary>A wall that blows open when its ambush starts.</summary>
    SpawnWall,

    /// <summary>A hole monsters come out of until it is blown up.</summary>
    MonsterHole,

    /// <summary>A broken wall the player walks through.</summary>
    BrokenWall,

    /// <summary>An energy fence frame without a fence.</summary>
    FenceFrame,

    /// <summary>An energy fence frame with a fence that stands until a generator dies.</summary>
    Fence
}

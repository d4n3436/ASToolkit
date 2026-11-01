namespace ASToolkit.Core.Levels;

/// <summary>
/// Specifies what stands on the edge between two neighboring cells.
/// </summary>
public enum EdgeKind : byte
{
    /// <summary>No edge.</summary>
    None,

    /// <summary>A wall.</summary>
    Wall,

    /// <summary>A working door.</summary>
    Door,

    /// <summary>The first half of a double door.</summary>
    DoubleDoor,

    /// <summary>The second half of a double door.</summary>
    DoubleDoorEnd,

    /// <summary>A door frame without a door, or a door that stays raised.</summary>
    Doorway,

    /// <summary>A door that never opens, closed, jammed or built into the wall.</summary>
    ShutDoor,

    /// <summary>A wall that opens, breakable or blown open by an ambush, or a monster hole.</summary>
    SecretWall,

    /// <summary>A wall that blows open when its ambush starts.</summary>
    SpawnWall,

    /// <summary>A broken wall the player walks through.</summary>
    BrokenWall,

    /// <summary>A military fence frame.</summary>
    FenceFrame
}

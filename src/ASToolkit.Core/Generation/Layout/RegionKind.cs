namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Defines the roles of the areas of a layout.
/// </summary>
public enum RegionKind
{
    /// <summary>A hall segment or room on the main route.</summary>
    Station,

    /// <summary>A dead-end room off a station.</summary>
    SideRoom,

    /// <summary>A hall segment or room of a gated branch off the route.</summary>
    Wing,

    /// <summary>A small room behind a breakable wall.</summary>
    SecretCloset,

    /// <summary>A small room behind a wall that blows open with its ambush.</summary>
    SpawnCloset
}

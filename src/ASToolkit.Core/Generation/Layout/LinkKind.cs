namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Defines how two areas of a layout connect.
/// </summary>
public enum LinkKind
{
    /// <summary>A door, one or two wall pieces wide.</summary>
    Door,

    /// <summary>An opening at least two units wide.</summary>
    Arch,

    /// <summary>The whole common boundary is open.</summary>
    Open,

    /// <summary>A breakable wall.</summary>
    SecretWall,

    /// <summary>A wall that blows open with its ambush.</summary>
    SpawnWall,

    /// <summary>An electric fence, one wall piece wide.</summary>
    Fence
}

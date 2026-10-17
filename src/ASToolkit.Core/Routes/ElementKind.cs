namespace ASToolkit.Core.Routes;

/// <summary>
/// Defines the gameplay elements the route analysis tracks.
/// </summary>
public enum ElementKind
{
    /// <summary>A placed monster.</summary>
    Monster,

    /// <summary>A boss.</summary>
    Boss,

    /// <summary>A scripted spawner.</summary>
    Spawner,

    /// <summary>An endless monster generator.</summary>
    EndlessGenerator,

    /// <summary>A monster teleport pad.</summary>
    TeleportPad,

    /// <summary>A dynamite site.</summary>
    DynamiteSite,

    /// <summary>The breaker.</summary>
    Breaker,

    /// <summary>A fence generator.</summary>
    Generator,

    /// <summary>An electric fence.</summary>
    Fence,

    /// <summary>A mounted cannon.</summary>
    MountedCannon,

    /// <summary>A breakable secret wall.</summary>
    SecretWall,

    /// <summary>A breakable wall over a monster hole that the breaker removes.</summary>
    PowerTrap,

    /// <summary>A wall that blows open with its ambush.</summary>
    SpawnWall,

    /// <summary>A monster hole in a wall.</summary>
    MonsterHole,

    /// <summary>A breakable crate.</summary>
    Crate,

    /// <summary>A pickup on the floor.</summary>
    Pickup
}

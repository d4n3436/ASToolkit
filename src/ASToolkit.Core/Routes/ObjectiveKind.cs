namespace ASToolkit.Core.Routes;

/// <summary>
/// Specifies the kinds of objectives on a tour, such as a dynamite site, a breaker, a generator or the exit.
/// </summary>
public enum ObjectiveKind
{
    /// <summary>Plant a charge at a dynamite site.</summary>
    DynamiteSite,

    /// <summary>Pull the breaker.</summary>
    Breaker,

    /// <summary>Destroy a fence generator.</summary>
    Generator,

    /// <summary>Kill a boss.</summary>
    Boss,

    /// <summary>Set off an ambush.</summary>
    Trigger,

    /// <summary>Meet the monsters an ambush released.</summary>
    Spawned,

    /// <summary>Meet a placed monster.</summary>
    Monsters,

    /// <summary>Pick up a dynamite charge.</summary>
    Dynamite,

    /// <summary>Pass the blackout trigger.</summary>
    Blackout,

    /// <summary>Reach the exit.</summary>
    Exit
}

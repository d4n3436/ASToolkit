namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents one action of a spawner program.
/// </summary>
/// <param name="Op">The action code.</param>
/// <param name="A">The first argument.</param>
/// <param name="B">The second argument.</param>
/// <param name="C">The third argument.</param>
public readonly record struct SpawnerAction(int Op, int A, int B, int C)
{
    /// <summary>Represents a move to (A, B), copied to every spawned monster.</summary>
    public const int Move = 33;

    /// <summary>Represents spawning monster A.</summary>
    public const int Build = 35;

    /// <summary>Represents a pause of A milliseconds.</summary>
    public const int Pause = 40;

    /// <summary>Represents waiting until the player is within C of (A, B).</summary>
    public const int Trigger = 43;

    /// <summary>Represents the end of the action stack.</summary>
    public const int Stop = 73;
}

namespace ASToolkit.Core.Rules;

/// <summary>
/// Represents the measurements of a level that the rules use besides its tour.
/// </summary>
public sealed class LevelMeasurement
{
    /// <summary>Gets the gating style.</summary>
    public required LevelStyle Style { get; init; }

    /// <summary>Gets the share of the floor that solid objects block.</summary>
    public required float BlockedShare { get; init; }

    /// <summary>Gets the number of breakable objects.</summary>
    public required int Breakables { get; init; }

    /// <summary>Gets the total hit points of every monster, placed or spawned.</summary>
    public required int HitPoints { get; init; }

    /// <summary>Gets the share of the hit points that spawners bring.</summary>
    public required float SpawnedShare { get; init; }

    /// <summary>Gets the small rooms with items behind breakable walls.</summary>
    public required IReadOnlyList<SecretCloset> SecretClosets { get; init; }

    /// <summary>Gets the number of breakable walls between two areas reachable without them.</summary>
    public required int Shortcuts { get; init; }
}

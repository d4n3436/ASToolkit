namespace ASToolkit.Core.Generation;

/// <summary>
/// Represents what a generated level's script sets up.
/// </summary>
public sealed class LevelScript
{
    /// <summary>
    /// Gets the music definitions copied from the original script.
    /// </summary>
    public required IReadOnlyList<string> MusicLines { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the doors are open at the start.
    /// </summary>
    public bool DoorsOpen { get; set; } = true;

    /// <summary>
    /// Gets or sets the point that turns the lights off and locks the doors, or null.
    /// </summary>
    public (double X, double Y)? Blackout { get; set; }

    /// <summary>
    /// Gets or sets the point that moves the respawn point, or null.
    /// </summary>
    public (double X, double Y)? Checkpoint { get; set; }
}

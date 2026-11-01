using System.Drawing;
using ASToolkit.Core.Routes;

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
    public PointF? Blackout { get; set; }

    /// <summary>
    /// Gets or sets the point that moves the respawn point, or null.
    /// </summary>
    public PointF? Checkpoint { get; set; }

    /// <summary>
    /// Gets or sets the area that wins the level and loads the shop when the player reaches it, or null when the level is won by
    /// killing every monster.
    /// </summary>
    public TriggerArea? Exit { get; set; }
}

using ASToolkit.Core.Rendering;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents what the map viewer draws over a map, in map pixels.
/// </summary>
public sealed class MapOverlay
{
    /// <summary>
    /// Gets the script events and the quest order of the level.
    /// </summary>
    public required LevelLogic Logic { get; init; }

    /// <summary>
    /// Gets the sprites the game never shows.
    /// </summary>
    public required IReadOnlyList<HelperMarker> Helpers { get; init; }

    /// <summary>
    /// Gets the walked tour of the level, or null when the level has none.
    /// </summary>
    public RouteAnalysis? Route { get; init; }

    /// <summary>
    /// Gets the stretches of the walked tour by gate state.
    /// </summary>
    public IReadOnlyList<TourRun> TourRuns { get; init; } = [];

    /// <summary>
    /// Gets the walk through the quest steps of the level, or null when the level has none.
    /// </summary>
    public WalkedRoute? Walk { get; init; }
}

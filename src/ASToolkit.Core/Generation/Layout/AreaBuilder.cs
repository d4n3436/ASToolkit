using ASToolkit.Core.Generation.Mission;
using ASToolkit.Core.Reference;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Provides the layout of a mission graph as spines of path rooms, each branch tree a block of rooms along its path room's sides or attached
/// to the room on its own.
/// </summary>
public sealed class AreaBuilder
{
    private const int LayoutAttempts = 4;

    private readonly AreaModel _areas;
    private readonly Random _rng;

    /// <summary>
    /// Initializes a new instance of the <see cref="AreaBuilder"/> class.
    /// </summary>
    /// <param name="areas">The architecture fitted to the style's originals.</param>
    /// <param name="rng">The random source of the layout stage.</param>
    public AreaBuilder(AreaModel areas, Random rng)
    {
        _areas = areas;
        _rng = rng;
    }

    /// <summary>
    /// Gets the counts kept over this builder's builds.
    /// </summary>
    public AreaBuildCounts Counts { get; } = new();

    /// <summary>
    /// Builds the layout plan of a mission graph, or returns null when a room finds no place after every redraw.
    /// </summary>
    public LayoutPlan? Build(MissionGraph mission, AreaRequest request)
    {
        var model = _areas.GetTheme(request.Theme);
        BlockResult? result = null;
        for (int attempt = 0; attempt < LayoutAttempts; attempt++)
        {
            if (attempt > 0)
            {
                Counts.LayoutRedraws++;
            }

            result = new BlockLayout(model, request.Theme, _rng, Counts).Lay(mission, request.FloorPixels / model.PixelsPerUnit);
            if (result.Failures == 0)
            {
                break;
            }
        }

        Counts.Layouts++;
        if (result is null || result.Failures > 0)
        {
            Counts.FailedBuilds++;
            return null;
        }

        Counts.EntrancesDrawn += result.EntrancesDrawn;
        Counts.EntrancesBuilt += result.EntrancesBuilt;
        Counts.EntrancesMoved += result.EntrancesMoved;
        Counts.LoopsDrawn += result.LoopsDrawn;
        Counts.LoopsBuilt += result.LoopsBuilt;
        Counts.LoopsMoved += result.LoopsMoved;
        Counts.Rings += result.Rings;
        new RoomTexturer(model, request.Theme, _rng, Counts).Apply(result);
        return new PlanWriter(request.Theme, _rng).Write(mission, result, request);
    }
}

using System.Numerics;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Routes;

/// <summary>
/// Represents the tour of a level and where its gameplay elements sit on it.
/// </summary>
public sealed class RouteAnalysis
{
    /// <summary>
    /// Gets the level name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the theme.
    /// </summary>
    public required LevelTheme Theme { get; init; }

    /// <summary>
    /// Gets the length of one cell step in pixels.
    /// </summary>
    public required double Step { get; init; }

    /// <summary>
    /// Gets the number of cells reachable from the start.
    /// </summary>
    public required int ReachableCells { get; init; }

    /// <summary>
    /// Gets the walked length of the tour in pixels.
    /// </summary>
    public required int RoutePixels { get; init; }

    /// <summary>
    /// Gets the shortest walking distance from the start to the farthest cell of the tour, in pixels.
    /// </summary>
    public required int SpinePixels { get; init; }

    /// <summary>
    /// Gets the share of the tour that walks over cells it already covered.
    /// </summary>
    public required double RevisitShare { get; init; }

    /// <summary>
    /// Gets the tour's stretches, merged by gate state.
    /// </summary>
    public required IReadOnlyList<RouteLeg> Legs { get; init; }

    /// <summary>
    /// Gets the objectives in completion order.
    /// </summary>
    public required IReadOnlyList<RouteObjective> Objectives { get; init; }

    /// <summary>
    /// Gets the gameplay elements.
    /// </summary>
    public required IReadOnlyList<RouteElement> Elements { get; init; }

    /// <summary>
    /// Gets the gate measurements.
    /// </summary>
    public required GateMeasurement Gating { get; init; }

    /// <summary>
    /// Gets the checkpoints the tour reaches.
    /// </summary>
    public required IReadOnlyList<RouteCheckpoint> Checkpoints { get; init; }

    /// <summary>
    /// Gets what the tour had to force.
    /// </summary>
    public required FallbackUsage FallbackUsage { get; init; }

    /// <summary>
    /// Gets the walked cells in order.
    /// </summary>
    public required IReadOnlyList<Cell> Tour { get; init; }

    /// <summary>
    /// Gets the routing grid.
    /// </summary>
    public required RouteGrid Grid { get; init; }

    /// <summary>
    /// Returns the tour split into stretches by gate state, splitting it in proportion to the lengths of the legs.
    /// </summary>
    public TourRun[] GetTourRuns()
    {
        var points = Tour.Select(cell => Grid.Lattice.GetCenter(cell)).Select(center => new Vector2((float)center.X, (float)center.Y)).ToArray();
        if (points.Length < 2)
        {
            return [];
        }

        double[] lengths = new double[points.Length];
        for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
        {
            lengths[pointIndex] = lengths[pointIndex - 1] + Vector2.Distance(points[pointIndex], points[pointIndex - 1]);
        }

        double legTotal = Math.Max(1, Legs.Sum(leg => leg.Pixels));
        double total = Math.Max(1e-9, lengths[^1]);
        var runs = new List<TourRun>();
        int start = 0;
        var current = GetPhase(0);
        for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
        {
            var phase = GetPhase(pointIndex);
            if (phase != current || pointIndex == points.Length - 1)
            {
                runs.Add(new TourRun(current, points[start..(pointIndex + 1)]));
                start = pointIndex;
                current = phase;
            }
        }

        return runs.ToArray();

        RoutePhase GetPhase(int point)
        {
            double share = lengths[point] / total;
            double walked = 0;
            var phase = RoutePhase.Free;
            foreach (var leg in Legs)
            {
                phase = leg.Phase;
                walked += leg.Pixels / legTotal;
                if (share < walked)
                {
                    break;
                }
            }

            return phase;
        }
    }
}

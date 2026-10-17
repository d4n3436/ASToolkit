using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;

namespace ASToolkit.Cli.Analysis;

// One level's route analysis as routes.json stores it; the tour is a list of [x, y] world points.
internal sealed record RouteReport(
    string Map,
    bool Generated,
    string? Image,
    LevelTheme Theme,
    int TourPixels,
    int SpinePixels,
    double SpineShare,
    double Elongation,
    double RevisitShare,
    int ReachableCells,
    IReadOnlyList<RouteLeg> Legs,
    IReadOnlyList<RouteObjective> Objectives,
    IReadOnlyList<RouteCheckpoint> Checkpoints,
    IReadOnlyList<ElementReport> Elements,
    GateMeasurement Gating,
    FallbackUsage Fallback,
    int[][] Tour);

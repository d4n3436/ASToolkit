using ASToolkit.Core.Routes;

namespace ASToolkit.Cli.Analysis;

// Trigger is [x, y, radius] for spawners that have one
internal sealed record ElementReport(
    ElementKind Kind,
    int Id,
    int X,
    int Y,
    bool Hidden,
    int CrateItem,
    double Progress,
    int Distance,
    int BranchDepth,
    int[] Trigger);

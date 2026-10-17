using ASToolkit.Core.Routes;

namespace ASToolkit.Cli.Analysis;

// A gameplay element of a level in routes.json; the trigger is [x, y, radius] for spawners that have one.
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

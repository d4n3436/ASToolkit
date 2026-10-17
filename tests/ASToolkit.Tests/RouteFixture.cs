using System.Text.Json;

namespace ASToolkit.Tests;

internal sealed record RouteFixture(
    string Name,
    string Theme,
    int CoarseCells,
    int CoarseEdges,
    int Cells,
    int RoutePx,
    double RevisitShare,
    Dictionary<string, JsonElement> Gates,
    IReadOnlyList<LegFixture> Legs,
    IReadOnlyList<ObjectiveFixture> Objectives,
    IReadOnlyList<CheckpointFixture> Checkpoints);

internal sealed record LegFixture(string Phase, int Px);

internal sealed record ObjectiveFixture(string Kind, double X, double Y, double P);

internal sealed record CheckpointFixture(double X, double Y, double P);

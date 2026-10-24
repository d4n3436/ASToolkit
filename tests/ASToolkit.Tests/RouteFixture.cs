using System.Text.Json;

namespace ASToolkit.Tests;

internal sealed record RouteFixture(
    string Name,
    string Theme,
    int CoarseCells,
    int CoarseEdges,
    int Cells,
    int RoutePx,
    float RevisitShare,
    Dictionary<string, JsonElement> Gates,
    IReadOnlyList<LegFixture> Legs,
    IReadOnlyList<ObjectiveFixture> Objectives,
    IReadOnlyList<CheckpointFixture> Checkpoints);

internal sealed record LegFixture(string Phase, int Px);

internal sealed record ObjectiveFixture(string Kind, float X, float Y, float P);

internal sealed record CheckpointFixture(float X, float Y, float P);

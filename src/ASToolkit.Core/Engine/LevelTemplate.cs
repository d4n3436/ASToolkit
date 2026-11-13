using System.Numerics;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

internal sealed class LevelTemplate
{
    public required LevelModel Model { get; init; }

    public required LevelScript Script { get; init; }

    public required ObjectCatalog Kinds { get; init; }

    public required MovementMap Movement { get; init; }

    public required WorldBounds Bounds { get; init; }

    public required IReadOnlyList<int> AmmoBatches { get; init; }

    public required IReadOnlyList<int> FrameTimes { get; init; }

    public Vector2 FallbackStart { get; init; }
}

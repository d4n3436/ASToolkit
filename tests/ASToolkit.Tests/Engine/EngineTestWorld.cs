using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Simulation;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Tests.Engine;

internal static class EngineTestWorld
{
    public static GameWorld CreateWorld(ObjectCatalog kinds, ReplayRecorder? recorder = null, PlayerLoadout? loadout = null, IReadOnlyList<MergedMapSprite>? sprites = null, int frameTime = GameClock.DefaultFrameTime)
    {
        var model = new LevelModel
        {
            Name = "Engine test",
            WallSet = WallSet.Basement,
            Lattice = new Lattice(20, 14),
            Cells = new HashSet<Cell> { new(0, 0), new(400, 400) },
            Edges = new Dictionary<EdgeKey, EdgeKind>(),
            Objects = [],
            Sprites = sprites ?? []
        };

        var level = new LevelTemplate
        {
            Model = model,
            Script = new LevelScript(),
            Kinds = kinds,
            Movement = new MovementMap(),
            Bounds = WorldBounds.Measure(model),
            AmmoBatches = new int[10],
            FrameTimes = [frameTime],
            FallbackStart = new Vector2(1000)
        };

        return new GameWorld(level, loadout ?? new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default), new GameRandom(1), recorder);
    }
}

using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Simulation;

namespace ASToolkit.Tests.Engine;

internal static class EngineTestMap
{
    private const string ScriptPath = "maps\\engine_test.lgc";
    private const int StartHandle = int.MaxValue;

    private const string Script = """
        #define MUSIC0 "music\\mus01.ogg"
        #define MUSIC1 "music\\mus02.ogg"
        #include "maps\export.lgc"
        #include "maps\common.lgc"
        #include "maps\common_compaign.lgc"
        main()
        {
          iff(1)
            InitGame();
          if( GameTact() )
            return;
          CompaignGameTact();
        }
        """;

    public static Map CreateMap(SpriteCatalog definitions, PlayerLoadout? loadout = null, IReadOnlyList<MergedMapSprite>? sprites = null, int frameTime = GameClock.DefaultFrameTime,
        MapEnvironment environment = MapEnvironment.None, DemoResource? demo = null, string? blood = null, GameSession? session = null)
    {
        var placed = new List<MergedMapSprite> { new(StartHandle, KnownObject.PlayerStart, new Vector3(1000, 1000, 0), default, Army.Player, []) };
        placed.AddRange(sprites ?? []);
        var model = new LevelModel
        {
            Name = "Engine test",
            WallSet = WallSet.Basement,
            Lattice = new Lattice(20, 14),
            Cells = new HashSet<Cell> { new(0, 0), new(400, 400) },
            Edges = new Dictionary<EdgeKey, EdgeKind>(),
            Objects = [],
            Sprites = placed,
            StoredSprites = placed,
            Environment = environment
        };

        var level = new LevelTemplate
        {
            Model = model,
            Definitions = definitions,
            Movement = new MovementMap(),
            AmmoBatches = new int[10],
            FrameTimes = [frameTime],
            FileSystem = new ScriptOverlay(new GameFolder(TestGameFolder.RequireFolder()), ScriptPath, Script),
            ScriptPath = ScriptPath
        };

        loadout ??= new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);

        var registry = Playthrough.CreateRegistry(TestGameFolder.RequireFolder(), "Level_02.map", loadout);
        if (blood is not null)
        {
            registry.Set("Blood", blood);
        }

        return new Map(level, new GameRandom(1), demo: demo, registry: registry, session: session);
    }
}

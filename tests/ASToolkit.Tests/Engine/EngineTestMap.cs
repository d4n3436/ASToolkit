using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
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
        MapEnvironment environment = MapEnvironment.None, DemoResource? demo = null, string? blood = null, GameSession? session = null, string script = Script, Gamma ambient = default, bool placesPlayer = true)
    {
        var placed = new List<MergedMapSprite>();
        if (placesPlayer)
        {
            placed.Add(new MergedMapSprite(StartHandle, KnownObject.PlayerStart, new Vector3(1000, 1000, 0), default, Army.Player, []));
        }

        placed.AddRange(sprites ?? []);
        var mapData = new MapData
        {
            Sprites = placed,
            StoredSprites = placed,
            Environment = environment,
            Ambient = ambient
        };

        var level = new LevelTemplate
        {
            MapData = mapData,
            Definitions = definitions,
            Movement = new MovementMap(),
            AmmoBatches = new int[10],
            FrameTimes = [frameTime],
            FileSystem = new ScriptOverlay(new GameFolder(TestGameFolder.RequireFolder()), ScriptPath, script),
            ScriptPath = ScriptPath
        };

        loadout ??= new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default);

        var registry = Playthrough.CreateRegistry(TestGameFolder.RequireFolder(), "Level_02.map", loadout);
        if (blood is not null)
        {
            registry.Set("Blood", blood);
        }

        var map = new Map(level, new GameRandom(1), demo: demo, registry: registry, session: session);
        if (placesPlayer)
        {
            map.SetFlagman(0, FindPlacedMan(map));
        }

        return map;
    }

    public static Sprite FindPlacedMan(Map map)
    {
        foreach (var placement in map.Sprites)
        {
            if (map.Definitions.Find(placement.Id)?.Object.SpriteClass != SpriteClass.Man)
                continue;

            var point = placement.Position.AsVector2();
            foreach (var sprite in map.Collector.GetNear(point, point))
            {
                if (sprite.Definition.Object.SpriteClass == SpriteClass.Man)
                    return sprite;
            }
        }

        throw new InvalidOperationException("Expected a map that places a player.");
    }
}

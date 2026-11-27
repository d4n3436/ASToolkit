using System.Drawing;
using System.Numerics;
using System.Text;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Tests.Engine;

internal static class EngineTestMap
{
    private static readonly DisplayMode[] CaptureAdapterModes =
    [
        new(new Size(320, 200), VidPixelFormat.X8R8G8B8),
        new(new Size(320, 240), VidPixelFormat.X8R8G8B8),
        new(new Size(640, 350), VidPixelFormat.X8R8G8B8),
        new(new Size(640, 400), VidPixelFormat.X8R8G8B8),
        new(new Size(640, 480), VidPixelFormat.X8R8G8B8),
        new(new Size(720, 400), VidPixelFormat.X8R8G8B8),
        new(new Size(720, 480), VidPixelFormat.X8R8G8B8),
        new(new Size(768, 480), VidPixelFormat.X8R8G8B8),
        new(new Size(800, 500), VidPixelFormat.X8R8G8B8),
        new(new Size(800, 600), VidPixelFormat.X8R8G8B8),
        new(new Size(864, 486), VidPixelFormat.X8R8G8B8),
        new(new Size(928, 580), VidPixelFormat.X8R8G8B8),
        new(new Size(960, 600), VidPixelFormat.X8R8G8B8),
        new(new Size(1024, 576), VidPixelFormat.X8R8G8B8),
        new(new Size(1024, 768), VidPixelFormat.X8R8G8B8),
        new(new Size(320, 200), VidPixelFormat.R5G6B5),
        new(new Size(320, 240), VidPixelFormat.R5G6B5),
        new(new Size(640, 350), VidPixelFormat.R5G6B5),
        new(new Size(640, 400), VidPixelFormat.R5G6B5),
        new(new Size(640, 480), VidPixelFormat.R5G6B5),
        new(new Size(720, 400), VidPixelFormat.R5G6B5),
        new(new Size(720, 480), VidPixelFormat.R5G6B5),
        new(new Size(768, 480), VidPixelFormat.R5G6B5),
        new(new Size(800, 500), VidPixelFormat.R5G6B5),
        new(new Size(800, 600), VidPixelFormat.R5G6B5),
        new(new Size(864, 486), VidPixelFormat.R5G6B5),
        new(new Size(928, 580), VidPixelFormat.R5G6B5),
        new(new Size(960, 600), VidPixelFormat.R5G6B5),
        new(new Size(1024, 576), VidPixelFormat.R5G6B5),
        new(new Size(1024, 768), VidPixelFormat.R5G6B5)
    ];

    public const string MapPath = "maps\\engine_test.map";
    private const string ScriptPath = "maps\\engine_test.lgc";
    public const uint StartRealTime = 1;
    private const string DemoPath = "maps\\engine_test.dem";
    private const int MapVersion = 12;
    private const float MapSide = 8192;
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

    public static ReadOnlySpan<byte> MapName => "maps\\engine_test.map"u8;

    public static IEnumerable<Sprite> GetEffects(this Map map) => map.UpdatedSprites.Where(sprite =>
        sprite.Vid.Class is not (SpriteClass.Man or SpriteClass.Frame or SpriteClass.Text) && sprite.Vid.Id is not (KnownObject.HardwareGround or KnownObject.SmallShadow) &&
        !SpriteCollector.IsListed(sprite.Vid) && sprite.Owner?.Vid.Class != SpriteClass.Man);

    public static string ProgramPath => Playthrough.GetProgramPath(TestGameFolder.RequireFolder());

    public static Size CaptureScreenSize => new(1024, 768);

    public static ReadOnlyMemory<DisplayMode> CaptureDisplayModes => CaptureAdapterModes;

    public static GraphicsInit CaptureDisplaySettings => new(new[] { CaptureScreenSize }, (int[])[32], CaptureScreenSize, 32, true);

    public static Registry CreateCaptureRegistry()
    {
        var registry = new Registry(Registry.ReadDefaultPath(new GameFolder(TestGameFolder.RequireFolder()), ProgramPath));
        registry.SetNumber("ScreenX", CaptureScreenSize.Width);
        registry.SetNumber("ScreenY", CaptureScreenSize.Height);

        return registry;
    }

    public static Registry CreateRegistry(PlayerLoadout? loadout = null, string? blood = null)
    {
        var registry = Playthrough.CreateRegistry(TestGameFolder.RequireFolder(), "Level_02.map", loadout ?? new PlayerLoadout(0, new Dictionary<KnownObject, int>(), default));
        if (blood is not null)
        {
            registry.Set("Blood", blood);
        }

        return registry;
    }

    public static Map CreateMap(PlayerLoadout? loadout = null, IReadOnlyList<MergedMapSprite>? sprites = null, int frameTime = FramePacer.DefaultFrameTime,
        MapEnvironment environment = MapEnvironment.None, DemoResource? demo = null, string? blood = null, TimeProvider? time = null, bool writeLog = false, string script = Script, Gamma ambient = default, bool placePlayer = true, Vector2 camera = default, bool recordDemo = false)
    {
        var files = CreateFiles(sprites, environment, script, ambient, placePlayer, camera);

        var map = new MapSteam(new GameSession(files, ProgramPath, Playthrough.AdapterModes, CreateRegistry(loadout, blood), time ?? new SteppedClock(), writeLog) { Objects = TestGameFolder.Objects }, Playthrough.DisplaySettings, "");
        map.Pace([frameTime]);
        map.Input.PostMessage(WindowMessageId.ActivateApp, 1, 0);
        if (recordDemo)
        {
            map.SaveDemo(Encoding.ASCII.GetBytes(DemoPath));
        }
        if (demo is null)
        {
            map.LoadAt(MapName, StartRealTime);
        }
        else
        {
            files.Set(DemoPath, demo.Value.Data);
            map.LoadAt(Encoding.ASCII.GetBytes(DemoPath), StartRealTime);
        }

        if (placePlayer)
        {
            map.SetFlagman(0, FindPlacedMan(map));
        }

        return map;
    }

    public static MemoryWriteOverlay CreateFiles(IReadOnlyList<MergedMapSprite>? sprites = null, MapEnvironment environment = MapEnvironment.None, string script = Script, Gamma ambient = default,
        bool placePlayer = true, Vector2 camera = default)
    {
        var placed = new List<MergedMapSprite>();
        if (placePlayer)
        {
            placed.Add(new MergedMapSprite(StartHandle, KnownObject.PlayerLegs, new Vector3(1000, 1000, 0), default, Army.Player, []));
        }

        placed.AddRange(sprites ?? []);
        var map = new MapResourceBuilder
        {
            Sprites = placed,
            Size = new SizeF(MapSide, MapSide),
            Scroll = camera,
            Graphics = new MapGraphics(environment, ambient, default, 0),
            Version = MapVersion
        };

        var files = new MemoryWriteOverlay(new GameFolder(TestGameFolder.RequireFolder()));
        files.Set(MapPath, map.ToBytes());
        files.Set(ScriptPath, Encoding.ASCII.GetBytes(script));

        return files;
    }

    public static Sprite FindFirstSprite(this Map map, KnownObject id) => map.UpdatedSprites.First(sprite => sprite.Vid.Id == id);

    public static Sprite FindPlacedMan(Map map)
    {
        foreach (var sprite in map.Sprites.Listed)
        {
            if (sprite.Vid.Class == SpriteClass.Man)
                return sprite;
        }

        throw new InvalidOperationException("Could not find a player in the level.");
    }
}

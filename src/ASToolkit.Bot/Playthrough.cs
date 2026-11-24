using System.Globalization;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Routes;
using ASToolkit.Analysis.Scripting;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

/// <summary>
/// Provides the bot's playthroughs of levels and campaigns.
/// </summary>
public sealed class Playthrough
{
    private const int MaxAttempts = 3;
    private const string MapsFolder = "maps";
    private const string ScriptExtension = ".lgc";
    private const string DemoExtension = ".dem";
    private const int ViewSeed = 1;
    private const int SeedLevels = 64;
    private const string ConfigFile = "AlienShooter.cfg";
    private const int RegistryItemCount = 12;
    private const int LifeImplantItem = 0;
    private const int StrengthImplantItem = 1;
    private const int SpeedImplantItem = 2;
    private const int AccuracyImplantItem = 3;
    private const int LivesItem = 7;
    private const int FlashlightItem = 8;
    private const int RescueKitItem = 10;
    private const int DroneItem = 11;

    private readonly ShopBot _shop;
    private readonly string _gameFolder;
    private readonly GameFolder _fileSystem;
    private readonly MemoryWriteOverlay _files;
    private readonly SpriteCatalog _definitions;
    private readonly Dictionary<RouteAnalysis, LevelSetup> _setups = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Playthrough"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="table">The common table of the game.</param>
    public Playthrough(string gameFolder, ObjectTable objects, GameTable table)
    {
        _gameFolder = gameFolder;
        _fileSystem = new GameFolder(gameFolder);
        _files = new MemoryWriteOverlay(_fileSystem);
        _definitions = new SpriteCatalog(gameFolder, objects);
        _shop = new ShopBot(objects, _definitions, table);
    }

    /// <summary>
    /// Gets the frame lengths in milliseconds, repeated in order.
    /// </summary>
    public IReadOnlyList<int> FrameTimes { get; init; } = FramePacer.DisplayFrameTimes;

    private static ReadOnlySpan<byte> DefaultLevelScript => """
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
        """u8;


    private static ReadOnlySpan<byte> ItemsVariable => "PlayerItem"u8;

    private static ReadOnlySpan<byte> MoneyVariable => "PlayerMoney"u8;

    /// <summary>
    /// Loads a map file with the level script beside it, or the campaign's default script if there isn't one, and runs its first frame.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="definitions">The sprite definitions of the game.</param>
    /// <param name="mapFile">The path of the map file, inside or outside the game folder.</param>
    /// <returns>The map after its first frame.</returns>
    /// <exception cref="FileNotFoundException">The map file does not exist.</exception>
    public static Map LoadMap(string gameFolder, SpriteCatalog definitions, string mapFile)
    {
        if (!File.Exists(mapFile))
            throw new FileNotFoundException($"Could not find map file '{mapFile}'.", mapFile);

        var files = new MemoryWriteOverlay(new GameFolder(gameFolder));
        string relative = Path.GetRelativePath(gameFolder, mapFile);
        bool isInside = !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        string mapPath = (isInside ? relative : Path.Combine(MapsFolder, Path.GetFileName(mapFile))).Replace('/', '\\');
        string scriptFile = Path.ChangeExtension(mapFile, ScriptExtension);
        string scriptPath = Path.ChangeExtension(mapPath, ScriptExtension);
        files.Set(mapPath, File.ReadAllBytes(mapFile));
        files.Set(scriptPath, File.Exists(scriptFile) ? File.ReadAllBytes(scriptFile) : DefaultLevelScript.ToArray());
        var profile = CreateProfile(gameFolder);
        var map = new Map(definitions, files, profile, new GameSession(profile, time: new LoadingRedrawClock()));
        map.Input.PostMessage(WindowMessageId.ActivateApp, 1, 0);
        map.RequestLoad(GameTextEncoding.Cyrillic.GetBytes(mapPath));
        map.Step(ViewSeed, ViewSeed, out _);

        return map;
    }

    /// <summary>
    /// Creates the registry of the bot's player profile.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <returns>The registry.</returns>
    public static ScriptRegistry CreateProfile(string gameFolder)
    {
        var registry = new ScriptRegistry(ScriptRegistry.ReadDefaultPath(Path.Combine(gameFolder, ConfigFile)));
        registry.Set("PlayerSex", "male");
        registry.Set("StateBar", "1");
        registry.Set("DifficultyLevel", "1");
        registry.Set("Blood", "1");

        return registry;
    }

    /// <summary>
    /// Creates the registry that holds the player's state at the start of a level.
    /// </summary>
    /// <param name="gameFolder">The game folder.</param>
    /// <param name="mapName">The name of the map relative to the maps folder.</param>
    /// <param name="loadout">The equipment the player carries into the level.</param>
    /// <returns>The registry.</returns>
    public static ScriptRegistry CreateRegistry(string gameFolder, string mapName, PlayerLoadout loadout)
    {
        var registry = CreateProfile(gameFolder);
        ApplyLoadout(registry, mapName, loadout);

        return registry;
    }

    /// <summary>
    /// Plays a level along its tour.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">The weapons, ammunition, armor and implants the player carries into the level.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="record">Whether to record the run into a demo.</param>
    /// <returns>The outcome of the playthrough.</returns>
    public PlaythroughResult Run(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool record = false)
    {
        Map? map = null;

        return Run(analysis, loadout, seed, record, ref map);
    }

    /// <summary>
    /// Creates the map of a recorded playthrough, loads the level its demo names and plays the demo's first frame.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">The equipment the player carried into the level.</param>
    /// <param name="demo">The demo the playthrough recorded.</param>
    /// <returns>The map after the level's load and the demo's first frame; stepping it plays the rest of the demo.</returns>
    public Map LoadReplay(RouteAnalysis analysis, PlayerLoadout loadout, DemoResource demo)
    {
        GetSetup(analysis);
        string demoPath = Path.ChangeExtension(GetMapPath(analysis.Name), DemoExtension);
        _files.Set(demoPath, demo.Data);
        var registry = CreateRegistry(_gameFolder, analysis.Name, loadout);
        var map = new Map(_definitions, _files, registry, new GameSession(registry, time: new LoadingRedrawClock()));
        map.Input.PostMessage(WindowMessageId.ActivateApp, 1, 0);
        map.RequestLoad(GameTextEncoding.Cyrillic.GetBytes(demoPath));
        map.Step(GameClock.StartWallTime, 0, out _);

        return map;
    }

    /// <summary>
    /// Plays campaign levels in order and shops with each level's earnings before the next level.
    /// </summary>
    /// <param name="levels">The levels in campaign order.</param>
    /// <param name="endMoney">The reward for each supplied level, in the same order.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="state">The state before the first shop, or null to begin a new campaign without shopping.</param>
    /// <param name="record">A value indicating whether to record the levels for replay.</param>
    /// <param name="recordedLevel">The zero-based index of the level to record, or null to record every level.</param>
    /// <returns>The result of each level played and the state after the last one completed.</returns>
    public CampaignResult PlayCampaign(IReadOnlyList<RouteAnalysis> levels, IReadOnlyList<int> endMoney, int seed,
        CampaignState? state = null, bool record = false, int? recordedLevel = null)
    {
        var results = new List<PlaythroughResult>();
        Map? map = null;
        bool shouldShopFirst = state is not null;
        state ??= CampaignState.CreateInitial();
        for (int level = 0; level < levels.Count; level++)
        {
            int money = state.Money;
            int reward = level < endMoney.Count ? endMoney[level] : 0;
            int firstSeed = (seed * SeedLevels + level) * MaxAttempts;
            var loadout = level > 0 || shouldShopFirst
                ? _shop.Shop(ref money, state.Loadout.Ammo, state.Loadout.Implants, state.Loadout.Items, reward)
                : state.Loadout;
            bool recordLevel = record && (recordedLevel is null || recordedLevel == level);
            var result = Run(levels[level], loadout, firstSeed, recordLevel, ref map);
            for (int attempt = 1; attempt < MaxAttempts && !result.Survived; attempt++)
            {
                result = Run(levels[level], loadout, firstSeed + attempt, recordLevel, ref map);
            }

            results.Add(result);

            if (!result.Survived)
            {
                break;
            }

            var items = result.Items;

            if (items.ContainsKey(KnownObject.Dynamite))
            {
                var carriedItems = items.ToDictionary();
                carriedItems.Remove(KnownObject.Dynamite);
                items = carriedItems;
            }

            state = new CampaignState(money + result.Money + reward,
                new PlayerLoadout(0, result.Ammo, result.Implants) { Items = items });
        }

        return new CampaignResult(
            Levels: results,
            State: state,
            IsCompleted: results.Count == levels.Count && results.All(result => result.Survived));
    }

    internal PlaySession Start(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool record = false, Map? map = null)
    {
        var setup = GetSetup(analysis);

        if (map is null)
        {
            var registry = CreateRegistry(_gameFolder, analysis.Name, loadout);
            map = new Map(_definitions, _files, registry, new GameSession(registry, time: new LoadingRedrawClock()));
            map.Input.PostMessage(WindowMessageId.ActivateApp, 1, 0);
        }
        else
        {
            ApplyLoadout(map.Registry, analysis.Name, loadout);
        }

        if (record)
        {
            map.Session.IsRecordingNextMap = true;
        }

        map.RequestLoad(setup.MapName);
        map.Step((uint)seed, (uint)seed, out _);
        var field = new WalkField(analysis.Grid, setup.DoorsLocked, map.Movement, map.Collector, _definitions.Find(KnownObject.PlayerStart)!.Object);
        var bot = new PlayerBot(map, new FramePacer(FrameTimes), analysis, field);

        return new PlaySession(map, bot, analysis.Exit is null && !analysis.ShouldClearMonsters);
    }

    private static PlaythroughResult CreateResult(PlaySession session, PlayerLoadout loadout, bool record)
    {
        var map = session.Map;

        return new PlaythroughResult(loadout, map is { HasPlayer: true, Player.IsAlive: true } && session.HasWon, map.Time, session.Bot.FarthestProgress, session.Log.Kills, session.Log.Respawns, session.Log.MinHealth,
            map.ReadScriptNumber(MoneyVariable), ReadImplants(map), session.Log.TakenPickups, ReadItems(map), map.HasPlayer ? new PlayerState(map).GetRemainingAmmo() : [],
            session.Log.Timeline, record ? new DemoResource(map.FinishDemoRecording()) : null);
    }

    private static PlayerImplants ReadImplants(Map map) => new(
        map.ReadScriptNumber(ItemsVariable), map.ReadScriptNumber(ItemsVariable, StrengthImplantItem),
        map.ReadScriptNumber(ItemsVariable, SpeedImplantItem), map.ReadScriptNumber(ItemsVariable, AccuracyImplantItem));

    private static Dictionary<KnownObject, int> ReadItems(Map map)
    {
        var items = new Dictionary<KnownObject, int>();
        int lives = map.ReadScriptNumber(ItemsVariable, LivesItem);
        bool hasDrone = map.HasLivingMonster(KnownObject.FlyingDrone);

        if (lives > 0)
        {
            items[KnownObject.ExtraLife] = lives;
        }

        if (map.ReadScriptNumber(ItemsVariable, FlashlightItem) != 0)
        {
            items[KnownObject.Flashlight] = 1;
        }

        if (map.HasPlayer && map.Player.Items.Contains((int)KnownObject.Medkit))
        {
            items[KnownObject.Medkit] = 1;
        }

        if (hasDrone)
        {
            items[KnownObject.Drone] = 1;
        }

        return items;
    }

    private static string GetMapPath(string mapName) => MapsFolder + "\\" + mapName.Replace('/', '\\');

    private static void ApplyLoadout(ScriptRegistry registry, string mapName, PlayerLoadout loadout)
    {
        string folder = Path.GetDirectoryName(mapName) ?? "";
        registry.Set("GameType", folder.Equals("ADDON2", StringComparison.OrdinalIgnoreCase) ? "3" : folder.Equals("ADDON", StringComparison.OrdinalIgnoreCase) ? "2" : "0");
        registry.Set("LevelNumber", new string(Path.GetFileNameWithoutExtension(mapName).Where(char.IsAsciiDigit).ToArray()).TrimStart('0'));
        for (int slot = 0; slot < Sprite.WeaponSlots; slot++)
        {
            var weapon = (KnownObject)(Sprite.WeaponItemBase + slot);
            bool owned = slot == Sprite.Pistols || loadout.Ammo.ContainsKey(weapon);
            registry.Set("PlayerWeapon" + slot, owned ? "1" : "0");
            registry.Set("PlayerAmmo" + slot, loadout.Ammo.GetValueOrDefault(weapon).ToString(CultureInfo.InvariantCulture));
        }

        int[] items = new int[RegistryItemCount];
        items[LifeImplantItem] = loadout.Implants.Life;
        items[StrengthImplantItem] = loadout.Implants.Strength;
        items[SpeedImplantItem] = loadout.Implants.Speed;
        items[AccuracyImplantItem] = loadout.Implants.Accuracy;

        if (loadout.Armor != 0)
        {
            items[loadout.Armor - KnownObject.WornGreenArmor] = 1;
        }

        items[LivesItem] = loadout.Items.GetValueOrDefault(KnownObject.ExtraLife);
        items[FlashlightItem] = loadout.Items.GetValueOrDefault(KnownObject.Flashlight);
        items[RescueKitItem] = loadout.Items.GetValueOrDefault(KnownObject.Medkit);
        items[DroneItem] = loadout.Items.GetValueOrDefault(KnownObject.Drone);
        for (int i = 0; i < items.Length; i++)
        {
            registry.Set("PlayerItem" + i, items[i].ToString(CultureInfo.InvariantCulture));
        }
    }

    private PlaythroughResult Run(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool record, ref Map? map)
    {
        var play = Start(analysis, loadout, seed, record, record ? null : map);
        map = play.Map;
        play.Play();

        return CreateResult(play, loadout, record);
    }

    private LevelSetup GetSetup(RouteAnalysis analysis)
    {
        if (!_setups.TryGetValue(analysis, out var setup))
        {
            setup = CreateSetup(analysis);
            _setups.Add(analysis, setup);
        }

        return setup;
    }

    private LevelSetup CreateSetup(RouteAnalysis analysis)
    {
        bool isLocked = analysis.DoorsInitiallyLocked ?? (analysis.Objectives.All(objective => objective.Kind != ObjectiveKind.Blackout) &&
            analysis.Objectives.Any(objective => objective.Kind == ObjectiveKind.Breaker) && analysis.Grid.Edges.Values.Any(kind => kind == EdgeKind.Door));
        string mapPath = GetMapPath(analysis.Name);
        string scriptPath = Path.ChangeExtension(mapPath, ScriptExtension);
        var model = analysis.Model;
        var file = new MapResourceBuilder
        {
            Sprites = model.StoredSprites.ToList(),
            Size = model.MapSize,
            Scroll = model.Camera,
            Graphics = new MapGraphics(model.Environment, model.Ambient, default, 0),
            Groups = model.Groups.Select(group => group.ToArray()).ToArray(),
            Version = model.Version
        };
        _files.Set(mapPath, file.ToBytes());
        if (!_fileSystem.TryRead(scriptPath, out _))
        {
            _files.Set(scriptPath, DefaultLevelScript.ToArray());
        }

        return new LevelSetup(GameTextEncoding.Cyrillic.GetBytes(mapPath), isLocked);
    }
}

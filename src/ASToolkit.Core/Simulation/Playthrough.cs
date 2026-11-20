using System.Globalization;
using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Routes;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides a tick simulation of a player bot playing a level with the game's combat rules.
/// </summary>
public sealed class Playthrough
{
    private const int MaxAttempts = 3;
    private const int SeedLevels = 64;

    private readonly ShopBot _shop;
    private const string ConfigFile = "AlienShooter.cfg";
    private const int RegistryItemCount = 12;
    private const int LifeImplantItem = 0;
    private const int StrengthImplantItem = 1;
    private const int SpeedImplantItem = 2;
    private const int AccuracyImplantItem = 3;
    private const int LivesItem = 7;
    private const int FlashlightItem = 8;
    private static ReadOnlySpan<byte> ItemsVariable => "PlayerItem"u8;
    private const int RescueKitItem = 10;
    private static ReadOnlySpan<byte> MoneyVariable => "PlayerMoney"u8;

    private const string DefaultLevelScript = """
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
    private const int DroneItem = 11;
    private readonly string _gameFolder;
    private readonly GameFolder _fileSystem;
    private readonly SpriteCatalog _definitions;
    private readonly IReadOnlyList<int> _ammoBatches;
    private readonly Dictionary<RouteAnalysis, LevelSetup> _setups = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Playthrough"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder, holding the sprites whose speed sets the animation times.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="table">The common table of the game, holding the ammunition batch of each weapon.</param>
    public Playthrough(string gameFolder, ObjectTable objects, GameTable table)
    {
        _gameFolder = gameFolder;
        _fileSystem = new GameFolder(gameFolder);
        _definitions = new SpriteCatalog(gameFolder, objects);
        _shop = new ShopBot(objects, _definitions, table);
        _ammoBatches = table.GetArray("GettingAmmo");
    }

    /// <summary>
    /// Gets the times in milliseconds the game clock advances by on successive frames, repeated in order.
    /// </summary>
    public IReadOnlyList<int> FrameTimes { get; init; } = GameClock.DisplayFrameTimes;

    /// <summary>
    /// Plays a level along its tour.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">The weapons, ammunition, armor and implants the player carries into the level.</param>
    /// <param name="seed">The seed of the random rolls.</param>
    /// <param name="record">Whether to record the run into a demo.</param>
    /// <param name="session">The map state earlier levels of the game session left, or null for a new session.</param>
    /// <returns>The outcome of the playthrough.</returns>
    public PlaythroughResult Run(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool record = false, GameSession? session = null)
    {
        var play = Start(analysis, loadout, seed, record, session);
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

    internal PlaySession Start(RouteAnalysis analysis, PlayerLoadout loadout, int seed, bool record = false, GameSession? session = null)
    {
        var setup = GetSetup(analysis);
        var registry = CreateRegistry(_gameFolder, analysis.Name, loadout);

        if (record)
        {
            session ??= new GameSession(registry);
            session.RecordsNextMap = true;
        }

        var map = new Map(setup.Level, new GameRandom(seed), registry: registry, session: session);
        var field = new WalkField(analysis.Grid, setup.DoorsLocked, map.Movement, map.Collector, _definitions.Find(KnownObject.PlayerStart)!.Object, analysis.Model.Sprites.Count);
        var bot = new PlayerBot(map, analysis, field);

        return new PlaySession(map, bot, analysis.Exit is null && !analysis.ClearsMonsters);
    }

    /// <summary>
    /// Creates the map of a recorded playthrough, set to play its demo from the start of the level.
    /// </summary>
    /// <param name="analysis">The route analysis of the level.</param>
    /// <param name="loadout">The equipment the player carried into the level.</param>
    /// <param name="demo">The demo the playthrough recorded.</param>
    /// <returns>The map, playing the demo as it steps.</returns>
    public Map LoadReplay(RouteAnalysis analysis, PlayerLoadout loadout, DemoResource demo)
    {
        var setup = GetSetup(analysis);
        return new Map(setup.Level, new GameRandom(demo.Seed), demo: demo, registry: CreateRegistry(_gameFolder, analysis.Name, loadout));
    }

    private static PlaythroughResult CreateResult(PlaySession session, PlayerLoadout loadout, bool record)
    {
        var map = session.Map;

        return new PlaythroughResult(loadout, map is { HasPlayer: true, Player.IsAlive: true } && session.Won, map.Time, session.Bot.FarthestProgress, session.Log.Kills, session.Log.Respawns, session.Log.MinHealth,
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
        bool hasDrone = map.Monsters.Any(sprite => sprite.Definition.Id == KnownObject.FlyingDrone && sprite.IsAlive);

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

    private LevelSetup CreateSetup(RouteAnalysis analysis)
    {
        bool isLocked = analysis.DoorsInitiallyLocked ?? (analysis.Objectives.All(objective => objective.Kind != ObjectiveKind.Blackout) &&
            analysis.Objectives.Any(objective => objective.Kind == ObjectiveKind.Breaker) && analysis.Grid.Edges.Values.Any(kind => kind == EdgeKind.Door));
        string scriptPath = GetScriptPath(analysis.Name);
        IGameFileSystem fileSystem = _fileSystem.TryRead(scriptPath, out _) ? _fileSystem : new ScriptOverlay(_fileSystem, scriptPath, DefaultLevelScript);
        var level = LevelTemplate.FromModel(analysis.Model, fileSystem, scriptPath, _definitions, _ammoBatches) with { FrameTimes = FrameTimes };

        return new LevelSetup(level, isLocked);
    }

    /// <summary>
    /// Plays campaign levels in order, shopping with each level's earnings before the next.
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
        bool shopsFirst = state is not null;
        state ??= CampaignState.CreateInitial();
        for (int level = 0; level < levels.Count; level++)
        {
            int money = state.Money;
            int reward = level < endMoney.Count ? endMoney[level] : 0;
            int firstSeed = (seed * SeedLevels + level) * MaxAttempts;
            var loadout = level > 0 || shopsFirst
                ? _shop.Shop(ref money, state.Loadout.Ammo, state.Loadout.Implants, state.Loadout.Items, reward)
                : state.Loadout;
            bool recordLevel = record && (recordedLevel is null || recordedLevel == level);
            var result = Run(levels[level], loadout, firstSeed, recordLevel);
            for (int attempt = 1; attempt < MaxAttempts && !result.Survived; attempt++)
            {
                result = Run(levels[level], loadout, firstSeed + attempt, recordLevel);
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

    /// <summary>
    /// Creates the registry of the bot's player profile.
    /// </summary>
    /// <param name="gameFolder">The game folder holding the game's configuration.</param>
    /// <returns>The registry.</returns>
    public static ScriptRegistry CreateProfile(string gameFolder)
    {
        var registry = new ScriptRegistry(ScriptRegistry.ReadDefaultPath(Path.Combine(gameFolder, ConfigFile)));
        registry.Set("PlayerSex", "male");
        registry.Set("StateBar", "1");
        registry.Set("DifficultyLevel", "1");

        return registry;
    }

    private static string GetScriptPath(string mapName) => Path.ChangeExtension("maps\\" + mapName.Replace('/', '\\'), ".lgc");

    /// <summary>
    /// Creates the registry a level's script reads the player's state from at the start of a level.
    /// </summary>
    /// <param name="gameFolder">The game folder holding the game's configuration.</param>
    /// <param name="mapName">The map's name relative to the maps folder, setting the game type and level number.</param>
    /// <param name="loadout">The equipment the player carries into the level.</param>
    /// <returns>The registry.</returns>
    public static ScriptRegistry CreateRegistry(string gameFolder, string mapName, PlayerLoadout loadout)
    {
        var registry = CreateProfile(gameFolder);
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
        for (int index = 0; index < items.Length; index++)
        {
            registry.Set("PlayerItem" + index, items[index].ToString(CultureInfo.InvariantCulture));
        }

        return registry;
    }

    private sealed record LevelSetup(LevelTemplate Level, bool DoorsLocked);
}

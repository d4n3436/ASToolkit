using System.Drawing;
using System.Numerics;
using System.Text;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one run of the game from startup to exit, holding the state that persists while levels are loaded and released.
/// </summary>
public sealed class GameSession : IDisposable
{
    /// <summary>
    /// Represents the screen width used when the registry doesn't hold one, in which case the game asks the player in its start dialog.
    /// </summary>
    public const int DefaultScreenWidth = 1024;

    /// <summary>
    /// Represents the screen height used when the registry doesn't hold one, in which case the game asks the player in its start dialog.
    /// </summary>
    public const int DefaultScreenHeight = 768;

    private const string ScreenWidthValue = "ScreenX";
    private const string ScreenHeightValue = "ScreenY";
    private const string SoundHighQualityValue = "SoundHighQuality";
    private const string FullScreenValue = "FullScreen";
    private const int DefaultFullScreen = 1;
    private const string Program = "AlienShooter.exe";
    private const string ConfigFile = "AlienShooter.cfg";
    private const string SetupFont = "Courier";
    private const int SetupFontWidth = 7;
    private const int SetupFontHeight = 8;
    private const string DefaultFont = "Arial";
    private const int DefaultFontWidth = 0;
    private const int DefaultFontHeight = 8;
    
    private bool _isConfigRead;
    
    private static ReadOnlySpan<byte> GameSection => "game"u8;
    
    private static ReadOnlySpan<byte> GraphSection => "graph"u8;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSession"/> class that reads the screen size from the registry and uses the default size if the registry doesn't hold one.
    /// </summary>
    /// <param name="registry">The registry the game reads its settings from.</param>
    /// <param name="fileSystem">The file system to write the log to and read the configuration file from, or <see langword="null"/> to not write logs and read the configuration later with <see cref="ReadConfig"/>.</param>
    /// <param name="time">The clock of the log's file name and timestamps, or <see langword="null"/> for the system clock.</param>
    /// <param name="desktop">The size of the desktop, or <see langword="null"/> if it is at least as large as the screen.</param>
    public GameSession(ScriptRegistry registry, IGameFileSystem? fileSystem = null, TimeProvider? time = null, Size? desktop = null)
    {
        Logger = new GameLogger(fileSystem, time ?? TimeProvider.System, Program);
        ScreenSize = new SizeF(registry.GetNumber(ScreenWidthValue, DefaultScreenWidth), registry.GetNumber(ScreenHeightValue, DefaultScreenHeight));
        IsFullScreen = registry.GetNumber(FullScreenValue, DefaultFullScreen) != 0;
        IsSoundHighQuality = registry.GetNumber(SoundHighQualityValue, 0) != 0;
        SetUpWindow(desktop ?? Size.Ceiling(ScreenSize));

        if (fileSystem is not null)
        {
            ReadConfig(fileSystem);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the game is running in debug mode.
    /// </summary>
    /// <remarks>In debug mode, the loading screen displays each step of a map load.</remarks>
    public bool IsDebugMode { get; private set; }

    /// <summary>
    /// Gets the indexes of the loading screen redraws that a recording shows the game making while it loads its sprite definitions at startup.
    /// </summary>
    /// <remarks>The redraws are counted in load order: one for each surface of a hardware sprite file and one for each definition.</remarks>
    public IReadOnlyList<int> StartupLoadingRedraws { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the game is running full screen.
    /// </summary>
    /// <remarks>When running in a window, the screen size is limited to the desktop size.</remarks>
    public bool IsFullScreen { get; }

    /// <summary>
    /// Reads the session's settings from the game's configuration file.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the configuration file.</param>
    /// <remarks>Calls after the first one have no effect.</remarks>
    public void ReadConfig(IGameFileSystem fileSystem)
    {
        if (_isConfigRead)
            return;

        _isConfigRead = true;
        int height = ProfileFile.GetInteger(fileSystem, ConfigFile, GraphSection, "FontSizeY"u8, DefaultFontHeight);
        int width = ProfileFile.GetInteger(fileSystem, ConfigFile, GraphSection, "FontSizeX"u8, DefaultFontWidth);
        string font = ProfileFile.TryGetString(fileSystem, ConfigFile, GraphSection, "Font"u8, out var name) ? Encoding.ASCII.GetString(name.Span) : DefaultFont;
        Graphics.CreateDeviceFont(font, width, height, SpriteCatalog.DefaultMaxTextureSize);
        IsDebugMode = ProfileFile.GetInteger(fileSystem, ConfigFile, GameSection, "DebugMode"u8, 0) != 0;
    }

    /// <summary>
    /// Gets a value indicating whether the player chose high-quality sound in the start dialog.
    /// </summary>
    public bool IsSoundHighQuality { get; }

    /// <summary>
    /// Gets the size of the screen in pixels.
    /// </summary>
    public SizeF ScreenSize { get; private set; }

    [EngineRoutine(0x0042f4b0)]
    private void SetUpWindow(Size desktop)
    {
        var viewport = new RectangleF(PointF.Empty, ScreenSize);

        if (!IsFullScreen && desktop.Width < ScreenSize.Width)
        {
            ScreenSize = ScreenSize with { Width = desktop.Width };
        }

        if (!IsFullScreen && desktop.Height < ScreenSize.Height)
        {
            ScreenSize = ScreenSize with { Height = desktop.Height };
        }

        Graphics.SetViewport(viewport);
        Graphics.CreateDeviceFont(SetupFont, SetupFontWidth, SetupFontHeight, SpriteCatalog.DefaultMaxTextureSize);
    }

    /// <summary>
    /// Gets or sets the speed the camera scrolls at along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether monster units may target neutral trains.
    /// </summary>
    public bool CanMonstersAttackNeutralTrains { get; set; }

    /// <summary>
    /// Gets or sets the sound effects and music of the session.
    /// </summary>
    public SoundSystem? Sound { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the next map load records the input of every frame into a demo kept in memory.
    /// </summary>
    public bool IsRecordingNextMap { get; set; }

    /// <summary>
    /// Gets the name of the map the session loaded last.
    /// </summary>
    public ReadOnlyMemory<byte> MapName { get; private set; }

    /// <summary>
    /// Gets the name of the map the session loaded before the last one.
    /// </summary>
    public ReadOnlyMemory<byte> PreviousMapName { get; private set; }

    /// <summary>
    /// Gets the screen effects, gamma and wind that persist across levels.
    /// </summary>
    public Graphics Graphics { get; } = new();

    internal GameLogger Logger { get; }

    internal uint MouseTipMovedAt { get; set; }

    internal Vector2 MouseTipCursor { get; set; }

    internal ReadOnlyMemory<byte> MouseTipText { get; set; }

    /// <summary>
    /// Closes the session's log and keeps it as the last run's log.
    /// </summary>
    public void Dispose() => Logger.Dispose();

    internal void EnterMap(ReadOnlyMemory<byte> name)
    {
        PreviousMapName = MapName;
        MapName = name;
    }
}

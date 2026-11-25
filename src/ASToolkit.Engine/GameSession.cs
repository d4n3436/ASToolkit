using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one run of the game from startup to exit and the state that persists while levels are loaded and released.
/// </summary>
public sealed class GameSession : IDisposable
{
    private const string ProgramName = "AlienShooter.exe";
    private const string ScreenWidthValue = "ScreenX";
    private const string ScreenHeightValue = "ScreenY";
    private const string FullScreenValue = "FullScreen";
    private const int DefaultFullScreen = 1;
    private const string BitsPerPixelValue = "BPP";
    private const int DefaultBitsPerPixel = 32;
    private const int DefaultVSync = 1;

    internal int TextureMemoryInUse;
    internal int PrevCurrentTime;
    internal int CurrentTime;
    internal uint PrevRealCurrentTime;
    internal uint RealCurrentTime;
    internal uint FrameRateStartedAt;
    internal int VidMemoryInUse;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSession"/> class that reads the screen size from the registry and uses the default size if the registry doesn't hold one.
    /// </summary>
    /// <param name="registry">The registry that holds the game's settings, or null for an empty one.</param>
    /// <param name="fileSystem">The file system that holds the configuration file and gets the log, or <see langword="null"/> to not write logs and use the default configuration.</param>
    /// <param name="time">The clock used for the log's file name and timestamps and for the wall time, or <see langword="null"/> for the system clock.</param>
    /// <param name="desktop">The size of the desktop, or <see langword="null"/> if it is at least as large as the screen.</param>
    /// <param name="windowRect">The bounds of the game window on the desktop, or <see langword="null"/> if it covers the screen.</param>
    /// <param name="clientRect">The bounds of the client area of the game window on the desktop, or <see langword="null"/> if it covers the screen.</param>
    /// <param name="hasAppMenu"><see langword="true"/> if the game window has the application menu; otherwise, <see langword="false"/>.</param>
    public GameSession(Registry? registry = null, IGameFileSystem? fileSystem = null, TimeProvider? time = null, Size? desktop = null, Rectangle? windowRect = null, Rectangle? clientRect = null, bool hasAppMenu = false)
    {
        Time = time ?? TimeProvider.System;
        Error = new MyError(fileSystem, Time, ProgramName);
        HasAppMenu = hasAppMenu;
        bool isVSync = (fileSystem is null ? DefaultVSync : Profile.GetInteger(fileSystem, Map.ConfigFile, Map.GraphSection, "VSync"u8, DefaultVSync)) != 0;
        Registry = registry ?? new Registry("");
        var screenSize = new SizeF(Registry.GetInt(ScreenWidthValue, Graphics.DefaultScreenWidth), Registry.GetInt(ScreenHeightValue, Graphics.DefaultScreenHeight));
        bool isColor32 = Registry.GetInt(BitsPerPixelValue, DefaultBitsPerPixel) == 32;
        bool isFullScreen = Registry.GetInt(FullScreenValue, DefaultFullScreen) != 0;
        Graphics = new Graphics(this)
        {
            ScreenSize = screenSize,
            IsFullScreen = isFullScreen,
        };
        Graphics.SetUpSettings(HasAppMenu, isVSync, isColor32, isFullScreen);
        var screen = Size.Ceiling(Graphics.ScreenSize);
        Graphics.Init(desktop ?? screen, windowRect ?? new Rectangle(Point.Empty, screen), clientRect ?? new Rectangle(Point.Empty, screen));
    }

    /// <summary>
    /// Gets or sets the speed the camera scrolls at along each axis in pixels per millisecond.
    /// </summary>
    public Vector2 ScrollSpeed { get; set; }

    /// <summary>
    /// Gets or sets the sound effects and music of the game.
    /// </summary>
    public Sound? Sound { get; set; }

    /// <summary>
    /// Gets or sets the tuning values of the game.
    /// </summary>
    public Constant? Constant { get; set; }

    /// <summary>
    /// Gets the graphics state that persists across levels.
    /// </summary>
    public Graphics Graphics { get; }

    /// <summary>
    /// Gets a value indicating whether the game window has the application menu.
    /// </summary>
    public bool HasAppMenu { get; }

    internal MyError Error { get; }

    internal Registry Registry { get; }

    internal uint MouseTipMovedAt { get; set; }

    internal Vector2 MouseTipCursor { get; set; }

    internal ReadOnlyMemory<byte> MouseTipText { get; set; }

    internal TimeProvider Time { get; }

    /// <summary>
    /// Closes the log and keeps it as the last run's log.
    /// </summary>
    public void Dispose() => Error.Dispose();

    internal uint GetWallTime() => (uint)(Time.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond);
}

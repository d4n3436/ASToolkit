using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one run of the game from startup to exit and the state that persists while levels are loaded and released.
/// </summary>
public sealed class GameSession : IDisposable
{
    internal readonly WeatherVertex[] Snowflakes = new WeatherVertex[Graphics.MaxSnowflakes * Graphics.SnowflakeVertexCount];
    internal readonly WeatherVertex[] Raindrops = new WeatherVertex[Graphics.MaxRaindrops * Graphics.RaindropVertexCount];
    internal bool IsTexturePowerOfTwoRequired = true;
    internal Size TextureMaxSize = new(256, 256);
    internal float SquallBaseSpeed = Graphics.NoSquall;
    internal float RainWindSpeed = Graphics.NoWind;
    internal float SnowWindSpeed = Graphics.NoWind;
    internal int TexturePaletteCount;
    internal int TextureMemoryInUse;
    internal bool IsTextureSquareRequired;
    internal bool IsTextureDynamicSupported;
    internal bool IsTexturePaletteSupported;
    internal bool IsTextureDxt1Supported;
    internal bool IsTextureDxt3Supported;
    internal bool IsTextureDxt5Supported;
    internal bool IsTextureAlphaPaletteSupported;
    internal bool IsTextureConditionalNonPowerOfTwoSupported;
    internal int PreviousCurrentTime;
    internal int CurrentTime;
    internal uint PreviousRealCurrentTime;
    internal uint RealCurrentTime;
    internal uint DemoPacedAt;
    internal int DemoPacedTime;
    internal Profile? Strings;
    internal ReadOnlyMemory<byte> ScriptText;
    internal Map? Map;
    internal int VidMemoryInUse;
    internal ControlSettings? Controls;
    internal GameRandom Random = new(0);
    internal byte SnowWindDirection;
    internal byte RainWindDirection;
    internal float RainWind;
    internal float SnowWind;
    internal int LoadingFrame;
    internal uint LoadingDrawnAt;
    internal int SquallStart;
    internal int GroundSnowStart;
    internal int GroundSnowAge;
    internal int RaindropCount;
    internal Vector2 SnowShift;
    internal int SnowflakeCount;
    internal int LightPhase;
    private const long MillisecondsPerSecond = 1000;
    private const string ScreenWidthValue = "ScreenX";
    private const string ScreenHeightValue = "ScreenY";
    private const string FullScreenValue = "FullScreen";
    private const int DefaultFullScreen = 1;
    private const string BitsPerPixelValue = "BPP";
    private const int DefaultBitsPerPixel = 32;
    private const int DefaultVSync = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSession"/> class that reads the screen size from the registry and uses the default size if the registry doesn't hold one.
    /// </summary>
    /// <param name="registry">The registry that holds the game's settings, or null for an empty one.</param>
    /// <param name="fileSystem">The file system that holds the configuration file and gets the log, or <see langword="null"/> to not write logs and use the default configuration.</param>
    /// <param name="time">The clock used for the log's file name and timestamps and for the real time, or <see langword="null"/> for the system clock.</param>
    /// <param name="desktop">The size of the desktop, or <see langword="null"/> if it is at least as large as the screen.</param>
    /// <param name="windowRect">The bounds of the game window on the desktop, or <see langword="null"/> if it covers the screen.</param>
    /// <param name="clientRect">The bounds of the client area of the game window on the desktop, or <see langword="null"/> if it covers the screen.</param>
    /// <param name="hasAppMenu"><see langword="true"/> if the game window has the application menu; otherwise, <see langword="false"/>.</param>
    public GameSession(Registry? registry = null, IGameFileSystem? fileSystem = null, TimeProvider? time = null, Size? desktop = null, Rectangle? windowRect = null, Rectangle? clientRect = null, bool hasAppMenu = false)
    {
        Time = time ?? TimeProvider.System;
        Error = new MyError(fileSystem, Time);
        HasAppMenu = hasAppMenu;
        int vSync = DefaultVSync;
        if (fileSystem is not null)
        {
            var config = new Profile(fileSystem);
            config.Load(Map.ConfigFile);
            vSync = config.GetInt(Map.GraphSection, "VSync"u8, DefaultVSync);
        }

        bool isVSync = vSync != 0;
        Registry = registry ?? new Registry("");
        var screenSize = new SizeF(Registry.GetInt(ScreenWidthValue, Graphics.DefaultScreenWidth), Registry.GetInt(ScreenHeightValue, Graphics.DefaultScreenHeight));
        bool isColor32 = Registry.GetInt(BitsPerPixelValue, DefaultBitsPerPixel) == 32;
        bool isFullScreen = Registry.GetInt(FullScreenValue, DefaultFullScreen) != 0;
        Graphics = new Graphics(this, screenSize, HasAppMenu, isVSync, isColor32, isFullScreen);
        var screen = Size.Ceiling(Graphics.ScreenSize);
        Graphics.Init(desktop ?? screen, windowRect ?? new Rectangle(Point.Empty, screen), clientRect ?? new Rectangle(Point.Empty, screen));
    }

    /// <summary>
    /// Gets or sets the speed the view scrolls at along each axis in pixels per millisecond.
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
    /// Gets or sets the shell that opens the files and links scripts launch.
    /// </summary>
    public IShellDevice? Shell { get; set; }

    /// <summary>
    /// Gets or sets the real time, in milliseconds, when the current frame rate measurement started.
    /// </summary>
    public uint FrameRateStartedAt { get; set; }

    /// <summary>
    /// Gets the graphics state that persists across levels.
    /// </summary>
    public Graphics Graphics { get; }

    /// <summary>
    /// Gets a value indicating whether the game window has the application menu.
    /// </summary>
    public bool HasAppMenu { get; }

    /// <summary>
    /// Gets the clock the game reads its real time from and the log takes its timestamps from.
    /// </summary>
    public TimeProvider Time { get; }

    internal MyError Error { get; }

    internal uint FadeStartedAt { get; set; }

    internal Registry Registry { get; }

    internal uint MouseTipMovedAt { get; set; }

    internal Vector2 MouseTipCursor { get; set; }

    internal ReadOnlyMemory<byte> MouseTipText { get; set; }

    internal bool IsTurningLegs { get; set; }

    internal Dictionary<(string Path, bool IsText), ReadOnlyMemory<byte>> ConvertedPictures { get; } = [];

    /// <summary>
    /// Closes the log and keeps it as the last run's log.
    /// </summary>
    public void Dispose() => Error.Dispose();

    internal static uint TimeGetTime(TimeProvider time) => (uint)(Math.BigMul(time.GetTimestamp(), MillisecondsPerSecond) / time.TimestampFrequency);

    internal uint TimeGetTime() => TimeGetTime(Time);
}

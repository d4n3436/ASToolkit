using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSession"/> class.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the game's files.</param>
    /// <param name="programPath">The path of the game's executable.</param>
    /// <param name="displayModes">The display modes the display adapter offers, in the adapter's order.</param>
    /// <param name="registry">The registry of the game, or <see langword="null"/> for an empty, in-memory one.</param>
    /// <param name="time">The clock used for the log's file name and timestamps and for the real time, or <see langword="null"/> for the system clock.</param>
    /// <param name="writeLog"><see langword="true"/> to write the log to the file system; otherwise, <see langword="false"/>.</param>
    public GameSession(IGameFileSystem fileSystem, string programPath, ReadOnlyMemory<DisplayMode> displayModes, RegistryFile? registry = null, TimeProvider? time = null, bool writeLog = true)
    {
        FileSystem = fileSystem;
        ProgramPath = programPath;
        DisplayModes = displayModes;
        Time = time ?? TimeProvider.System;
        Error = new MyError(writeLog ? fileSystem : null, Time);
        RegistryFile = registry ?? new RegistryFile();
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
    /// Gets the window the game runs in.
    /// </summary>
    public IWindowDevice? Window { get; init; }

    /// <summary>
    /// Gets the device that frames are drawn on.
    /// </summary>
    public IGraphicsDevice? GraphicsDevice { get; init; }

    /// <summary>
    /// Gets the object table the map starts with.
    /// </summary>
    public ObjectTable? Objects { get; init; }

    /// <summary>
    /// Gets the file system that holds the game's files.
    /// </summary>
    public IGameFileSystem FileSystem { get; }

    /// <summary>
    /// Gets the registry of the game.
    /// </summary>
    public RegistryFile RegistryFile { get; }

    /// <summary>
    /// Gets the path of the game's executable.
    /// </summary>
    public string ProgramPath { get; }

    /// <summary>
    /// Gets the display modes the display adapter offers, in the adapter's order.
    /// </summary>
    public ReadOnlyMemory<DisplayMode> DisplayModes { get; }

    /// <summary>
    /// Gets or sets the real time, in milliseconds, when the current frame rate measurement started.
    /// </summary>
    public uint FrameRateStartedAt { get; set; }

    /// <summary>
    /// Gets the graphics state that persists across levels.
    /// </summary>
    public Graphics Graphics { get; internal set; } = null!;

    /// <summary>
    /// Gets the clock the game reads its real time from and the log takes its timestamps from.
    /// </summary>
    public TimeProvider Time { get; }

    internal MyError Error { get; }

    internal uint FadeStartedAt { get; set; }

    internal Registry Registry { get; set; } = null!;

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

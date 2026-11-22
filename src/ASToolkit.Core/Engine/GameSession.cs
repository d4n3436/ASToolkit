using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.IO;

namespace ASToolkit.Core.Engine;

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
    private const string Program = "AlienShooter.exe";

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSession"/> class that reads the screen size from the registry and uses the default size if the registry doesn't hold one.
    /// </summary>
    /// <param name="registry">The registry the game reads its settings from.</param>
    /// <param name="fileSystem">The file system the log is written to, or <see langword="null"/> to write no log.</param>
    /// <param name="time">The clock of the log's file name and timestamps, or <see langword="null"/> for the system clock.</param>
    public GameSession(ScriptRegistry registry, IGameFileSystem? fileSystem = null, TimeProvider? time = null)
    {
        Logger = new GameLogger(fileSystem, time ?? TimeProvider.System, Program);
        ScreenSize = new SizeF(registry.GetNumber(ScreenWidthValue, DefaultScreenWidth), registry.GetNumber(ScreenHeightValue, DefaultScreenHeight));
        IsSoundHighQuality = registry.GetNumber(SoundHighQualityValue, 0) != 0;
    }

    /// <summary>
    /// Gets a value indicating whether the player chose high-quality sound in the start dialog.
    /// </summary>
    public bool IsSoundHighQuality { get; }

    /// <summary>
    /// Gets the size of the screen in pixels.
    /// </summary>
    public SizeF ScreenSize { get; }

    /// <summary>
    /// Gets the part of the screen the map shows.
    /// </summary>
    public RectangleF View => new(PointF.Empty, ScreenSize);

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
    /// Gets or sets a value indicating whether the system draws the mouse cursor rather than the game's cursor sprite.
    /// </summary>
    public bool IsCursorHardware { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the cursor images are loaded and the cursor is showing up.
    /// </summary>
    public bool IsCursorLoaded { get; set; } = true;

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

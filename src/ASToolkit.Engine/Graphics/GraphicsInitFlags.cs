namespace ASToolkit.Engine;

/// <summary>
/// Specifies the switches of the display settings the game starts with.
/// </summary>
[Flags]
public enum GraphicsInitFlags
{
    /// <summary>
    /// No switch is set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The game window has the application menu.
    /// </summary>
    AppMenu = 1 << 0,

    /// <summary>
    /// Presents wait for the display's vertical sync.
    /// </summary>
    VSync = 1 << 1,

    /// <summary>
    /// The start dialog shows every setting.
    /// </summary>
    FullStartDialog = 1 << 2
}

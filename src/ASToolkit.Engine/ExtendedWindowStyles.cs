namespace ASToolkit.Engine;

/// <summary>
/// Specifies the extended styles of the game window.
/// </summary>
[Flags]
public enum ExtendedWindowStyles
{
    /// <summary>
    /// No extended style.
    /// </summary>
    None = 0,

    /// <summary>
    /// The window has a taskbar button while it is visible.
    /// </summary>
    AppWindow = 1 << 18
}

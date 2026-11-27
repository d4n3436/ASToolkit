namespace ASToolkit.Engine;

/// <summary>
/// Specifies the styles of the game window.
/// </summary>
[Flags]
public enum WindowStyles : uint
{
    /// <summary>
    /// No style.
    /// </summary>
    None = 0,

    /// <summary>
    /// The window is shown as soon as it is created.
    /// </summary>
    Visible = 1 << 28,

    /// <summary>
    /// The window has no border or title bar.
    /// </summary>
    Popup = 1u << 31
}

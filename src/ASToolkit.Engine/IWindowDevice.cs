using System.Drawing;

namespace ASToolkit.Engine;

/// <summary>
/// Defines the window the game runs in and the queue of messages the window receives.
/// </summary>
public interface IWindowDevice
{
    /// <summary>
    /// Gets the size of the primary display in pixels.
    /// </summary>
    Size DesktopSize { get; }

    /// <summary>
    /// Gets the bounds of the window on the desktop.
    /// </summary>
    Rectangle WindowRect { get; }

    /// <summary>
    /// Gets the bounds of the window's client area on the desktop.
    /// </summary>
    Rectangle ClientRect { get; }

    /// <summary>
    /// Creates the window.
    /// </summary>
    /// <param name="extendedStyle">The extended styles of the window.</param>
    /// <param name="title">The title of the window.</param>
    /// <param name="style">The styles of the window.</param>
    /// <param name="bounds">The bounds of the window on the desktop.</param>
    void CreateWindowEx(ExtendedWindowStyles extendedStyle, ReadOnlySpan<byte> title, WindowStyles style, Rectangle bounds);

    /// <summary>
    /// Shows the window.
    /// </summary>
    /// <returns>The messages the window receives while it's shown.</returns>
    ReadOnlySpan<WindowMessage> ShowWindow();

    /// <summary>
    /// Paints the window if it needs painting.
    /// </summary>
    /// <returns>The messages the window receives while it's painted.</returns>
    ReadOnlySpan<WindowMessage> UpdateWindow();

    /// <summary>
    /// Shows or hides the cursor.
    /// </summary>
    /// <param name="show"><see langword="true"/> to show the cursor; <see langword="false"/> to hide it.</param>
    void ShowCursor(bool show);

    /// <summary>
    /// Attempts to remove the next message from the window's queue.
    /// </summary>
    /// <param name="message">The message if there is one; otherwise, the default message.</param>
    /// <returns><see langword="true"/> if a message was removed; otherwise, <see langword="false"/>.</returns>
    bool TryPeekMessage(out WindowMessage message);
}

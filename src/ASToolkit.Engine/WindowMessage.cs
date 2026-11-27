namespace ASToolkit.Engine;

/// <summary>
/// Represents a message sent or posted to the game window.
/// </summary>
/// <param name="Id">The message.</param>
/// <param name="WParam">The message's first parameter.</param>
/// <param name="LParam">The message's second parameter.</param>
public readonly record struct WindowMessage(WindowMessageId Id, int WParam, int LParam);

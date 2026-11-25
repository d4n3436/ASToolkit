namespace ASToolkit.Engine;

/// <summary>
/// Specifies how the view scrolls, using the <c>SCROLL_</c> flags of the script.
/// </summary>
[Flags]
public enum ScrollType
{
    /// <summary>
    /// The view does not scroll.
    /// </summary>
    None = 0,

    /// <summary>
    /// The view scrolls while the cursor rests on a screen border.
    /// </summary>
    FromBorder = 1 << 0,

    /// <summary>
    /// The view scrolls with the mouse.
    /// </summary>
    WithMouse = 1 << 1,

    /// <summary>
    /// The view eases toward the player.
    /// </summary>
    ToFlagman = 1 << 2,

    /// <summary>
    /// The view eases toward the player and leads toward the cursor.
    /// </summary>
    ToFlagmanWithDirection = 1 << 3,

    /// <summary>
    /// The view keeps the player in the center of the screen.
    /// </summary>
    FlagmanInCenter = 1 << 4,

    /// <summary>
    /// The view scrolls with the movement keys.
    /// </summary>
    WithArrows = 1 << 5
}

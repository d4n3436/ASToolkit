namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies how the camera scrolls, the <c>SCROLL_</c> flags of the script.
/// </summary>
[Flags]
public enum ScrollType
{
    /// <summary>
    /// The camera does not scroll.
    /// </summary>
    None = 0,

    /// <summary>
    /// The camera scrolls while the cursor rests on a screen border.
    /// </summary>
    FromBorder = 1,

    /// <summary>
    /// The camera scrolls with the mouse.
    /// </summary>
    WithMouse = 2,

    /// <summary>
    /// The camera eases toward the flagman.
    /// </summary>
    ToFlagman = 4,

    /// <summary>
    /// The camera eases toward the flagman, leading toward the cursor.
    /// </summary>
    ToFlagmanWithDirection = 8,

    /// <summary>
    /// The camera keeps the flagman in the center of the screen.
    /// </summary>
    FlagmanInCenter = 16,

    /// <summary>
    /// The camera scrolls with the movement keys.
    /// </summary>
    WithArrows = 32
}

namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Specifies a screen effect the script language starts.
/// </summary>
public enum ScreenEffect
{
    /// <summary>
    /// No effect, which stops every running effect.
    /// </summary>
    None = 0,

    /// <summary>
    /// A flash of a color.
    /// </summary>
    Nuke = 1,

    /// <summary>
    /// A smooth scroll of the view to a point.
    /// </summary>
    Scroll = 2,

    /// <summary>
    /// A smooth darkening of the screen, lasting 2,304 milliseconds by default.
    /// </summary>
    Fade = 3,

    /// <summary>
    /// An unfolding of the screen from a point to its full size.
    /// </summary>
    OpenWindow = 4,

    /// <summary>
    /// A smooth alpha blend from the previous screen.
    /// </summary>
    AlphaAppear = 5,

    /// <summary>
    /// An appearance of the screen pixel by pixel.
    /// </summary>
    PixelAppear = 6,

    /// <summary>
    /// A shift of the screen's lines.
    /// </summary>
    LineShift = 7,

    /// <summary>
    /// A scaling of the screen from a point to its full size.
    /// </summary>
    ScaleWindow = 8,

    /// <summary>
    /// A smooth darkening of the screen, lasting 1,280 milliseconds by default.
    /// </summary>
    FadeIn = 9,

    /// <summary>
    /// A smooth brightening of the screen out of darkness.
    /// </summary>
    FadeOut = 10,

    /// <summary>
    /// A smooth shift of the current palette to another.
    /// </summary>
    ShiftGamma = 11
}

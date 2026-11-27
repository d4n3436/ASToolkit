using System.Drawing;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the display settings the game starts with and the display modes it accepts.
/// </summary>
public struct GraphicsInit
{
    private const int MaxModes = 31;
    private const int MaxBitDepths = 7;

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsInit"/> struct.
    /// </summary>
    /// <param name="modes">The screen sizes the game accepts.</param>
    /// <param name="bitDepths">The color depths, in bits per pixel, the game accepts.</param>
    /// <param name="screenSize">The default screen size.</param>
    /// <param name="bitsPerPixel">The default color depth in bits per pixel.</param>
    /// <param name="isFullScreen"><see langword="true"/> to run in full screen by default; otherwise, <see langword="false"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="modes"/> has more than 31 sizes or <paramref name="bitDepths"/> has more than 7 depths.</exception>
    public GraphicsInit(ReadOnlyMemory<Size> modes, ReadOnlyMemory<int> bitDepths, Size screenSize, int bitsPerPixel, bool isFullScreen)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(modes.Length, MaxModes, nameof(modes));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitDepths.Length, MaxBitDepths, nameof(bitDepths));
        Modes = modes;
        BitDepths = bitDepths;
        ScreenSize = screenSize;
        BitsPerPixel = bitsPerPixel;
        IsFullScreen = isFullScreen;
    }

    /// <summary>
    /// Gets the screen sizes the game accepts.
    /// </summary>
    public readonly ReadOnlyMemory<Size> Modes { get; }

    /// <summary>
    /// Gets the color depths the game accepts, in bits per pixel.
    /// </summary>
    public readonly ReadOnlyMemory<int> BitDepths { get; }

    /// <summary>
    /// Gets or sets the switches of the settings.
    /// </summary>
    public GraphicsInitFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the index of the display adapter.
    /// </summary>
    public int Device { get; set; }

    /// <summary>
    /// Gets or sets the screen size.
    /// </summary>
    public Size ScreenSize { get; set; }

    /// <summary>
    /// Gets or sets the color depth in bits per pixel.
    /// </summary>
    public int BitsPerPixel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the game should run in full screen.
    /// </summary>
    public bool IsFullScreen { get; set; }

    /// <summary>
    /// Returns whether the game accepts a display mode.
    /// </summary>
    /// <param name="size">The screen size of the mode.</param>
    /// <param name="bits">The color depth of the mode in bits per pixel.</param>
    /// <returns><see langword="true"/> if both the size and the color depth are accepted; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x004348c0)]
    public readonly bool HasMode(Size size, int bits) => BitDepths.Span.Contains(bits) && Modes.Span.Contains(size);
}

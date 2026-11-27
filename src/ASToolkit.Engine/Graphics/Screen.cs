using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the back buffer and the software depth buffer that a frame is drawn into.
/// </summary>
public sealed class Screen
{
    private const int DepthSlackRows = 4;

    private readonly byte[] _colors;
    private ushort[]? _depth;

    /// <summary>
    /// Initializes a new instance of the <see cref="Screen"/> class.
    /// </summary>
    /// <param name="size">The size of the screen, in pixels.</param>
    /// <param name="isTrueColor"><see langword="true"/> for 32-bit pixels; <see langword="false"/> for 16-bit pixels.</param>
    /// <param name="format">The channel layout of 16-bit colors, or <see langword="null"/> for 5-6-5.</param>
    public Screen(Size size, bool isTrueColor, HighColorFormat? format = null)
    {
        Format = format ?? HighColorFormat.Rgb565;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);
        Size = size;
        IsTrueColor = isTrueColor;
        Pitch = size.Width;
        DepthWidth = size.Width;
        _colors = new byte[Pitch * size.Height * (isTrueColor ? sizeof(uint) : sizeof(ushort))];
        Viewport = new Rectangle(Point.Empty, size);
    }

    /// <summary>
    /// Gets or sets the area of the screen that drawing is limited to, in pixels.
    /// </summary>
    public Rectangle Viewport { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether textures are sampled with linear filtering.
    /// </summary>
    public bool IsLinearFiltered { get; set; }

    /// <summary>
    /// Gets or sets the palette of a screen used as a P8 texture, with colors packed as 0xAARRGGBB, or empty for other screens.
    /// </summary>
    public uint[] Palette { get; set; } = [];

    /// <summary>
    /// Gets the channel layout that 16-bit sprite colors are converted from and, on a 16-bit screen, the layout of the back buffer.
    /// </summary>
    public HighColorFormat Format { get; }

    /// <summary>
    /// Gets the size of the screen, in pixels.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets a value indicating whether the back buffer holds 32-bit pixels.
    /// </summary>
    public bool IsTrueColor { get; }

    /// <summary>
    /// Gets the number of pixels from the start of one back buffer row to the next.
    /// </summary>
    public int Pitch { get; }

    /// <summary>
    /// Gets the number of depth values from the start of one depth row to the next.
    /// </summary>
    public int DepthWidth { get; }

    /// <summary>
    /// Gets the 32-bit pixels of the back buffer, X8R8G8B8, row by row from the top.
    /// </summary>
    /// <exception cref="InvalidOperationException">The back buffer holds 16-bit pixels.</exception>
    public Span<uint> TrueColorPixels => IsTrueColor
        ? MemoryMarshal.Cast<byte, uint>(_colors)
        : throw new InvalidOperationException("The back buffer holds 16-bit pixels.");

    /// <summary>
    /// Gets the 16-bit pixels of the back buffer, row by row from the top.
    /// </summary>
    /// <exception cref="InvalidOperationException">The back buffer holds 32-bit pixels.</exception>
    public Span<ushort> HighColorPixels => !IsTrueColor
        ? MemoryMarshal.Cast<byte, ushort>(_colors)
        : throw new InvalidOperationException("The back buffer holds 32-bit pixels.");

    /// <summary>
    /// Gets the software depth buffer, row by row from the top.
    /// </summary>
    public Span<ushort> Depth => _depth ??= new ushort[DepthWidth * (Size.Height + DepthSlackRows)];

    internal GammaTable GammaTable { get; } = new();

    internal void CopyColorsTo(Screen target) => _colors.CopyTo(target._colors, 0);
}

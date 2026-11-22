using System.Drawing;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the back buffer and the software depth buffer that a frame is drawn into.
/// </summary>
public sealed class Screen
{
    internal const int LightBufferSize = 256;
    private const int LightBufferLength = LightBufferSize * LightBufferSize;

    private readonly byte[] _colors;
    private readonly ushort[] _depth;
    private readonly ushort[] _snowBuffer = new ushort[LightBufferLength];
    private readonly ushort[][] _lightBuffers = [new ushort[LightBufferLength], new ushort[LightBufferLength]];
    private ushort[] _scratch = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Screen"/> class.
    /// </summary>
    /// <param name="size">The size of the screen, in pixels.</param>
    /// <param name="isTrueColor"><see langword="true"/> for 32-bit pixels; <see langword="false"/> for 16-bit pixels.</param>
    /// <param name="format">The channel layout of the device's 16-bit textures, which 16-bit colors follow, or <see langword="null"/> for 5-6-5.</param>
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
        _depth = new ushort[DepthWidth * size.Height];
        Clip = new Rectangle(Point.Empty, size);
    }

    /// <summary>
    /// Gets or sets the area that drawing is limited to, in screen pixels.
    /// </summary>
    public Rectangle Clip { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether textures are sampled with linear filtering, the state the last quad drawn leaves for the meshes after it: linear when the quad was scaled.
    /// </summary>
    public bool IsLinearFiltered { get; set; }

    /// <summary>
    /// Gets or sets the colors, packed as 0xAARRGGBB, of a screen used as a P8 texture, whose 16-bit pixels then hold palette indexes; empty for other screens.
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
    public Span<ushort> Depth => _depth;

    /// <summary>
    /// Writes the back buffer as opaque pixels, row by row from the top.
    /// </summary>
    /// <param name="pixels">The pixels to write, at least as many as the screen holds.</param>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is smaller than the screen.</exception>
    public void CopyTo(Span<Bgra32> pixels)
    {
        int count = Size.Width * Size.Height;
        if (pixels.Length < count)
            throw new ArgumentException("The pixel buffer is smaller than the screen.", nameof(pixels));

        if (IsTrueColor)
        {
            var colors = TrueColorPixels[..count];
            for (int i = 0; i < count; i++)
            {
                pixels[i] = ToBgra(colors[i]);
            }
        }
        else
        {
            var colors = HighColorPixels[..count];
            for (int i = 0; i < count; i++)
            {
                pixels[i] = ToBgra(Format.Expand(colors[i]));
            }
        }
    }

    private static Bgra32 ToBgra(uint color) => new((byte)(color >> 16), (byte)(color >> 8), (byte)color, byte.MaxValue);

    internal void CopyColorsTo(Screen target) => _colors.CopyTo(target._colors, 0);

    /// <summary>
    /// Gets the size of the scratch texture, in pixels.
    /// </summary>
    public Size ScratchSize { get; private set; }

    /// <summary>
    /// Gets the pixels of the scratch texture that sprites draw their frames to before the frames are blended onto the back buffer, in ARGB4444 and row by row from the top.
    /// </summary>
    public Span<ushort> Scratch => _scratch;

    /// <summary>
    /// Makes the scratch texture at least as large as an area.
    /// </summary>
    /// <param name="size">The size of the area, in pixels.</param>
    public void EnsureScratch(Size size)
    {
        if (size.Width <= ScratchSize.Width && size.Height <= ScratchSize.Height)
            return;

        ScratchSize = new Size(Math.Max(ScratchSize.Width, size.Width), Math.Max(ScratchSize.Height, size.Height));
        _scratch = new ushort[ScratchSize.Width * ScratchSize.Height];
    }

    /// <summary>
    /// Gets the snow buffer, the 256 by 256 texture that the ground snow is drawn from, with one texel for each 4 by 4 pixels of the screen.
    /// </summary>
    public Span<ushort> SnowBuffer => _snowBuffer;

    /// <summary>
    /// Gets the light buffer that the next light draws its intensities to, one of two buffers that lights use in turns, in RGB565 and 256 pixels wide and tall.
    /// </summary>
    public Span<ushort> LightBuffer => _lightBuffers[LightPhase];

    /// <summary>
    /// Gets or sets which of the two light buffers the next light draws to.
    /// </summary>
    public int LightPhase { get; set; }
}

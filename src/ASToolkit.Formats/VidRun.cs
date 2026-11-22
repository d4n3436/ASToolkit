using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a run of adjacent pixels of one sprite frame row.
/// </summary>
public readonly struct VidRun
{
    private readonly ReadOnlyMemory<byte> _depths;
    private readonly ReadOnlyMemory<byte> _colors;
    private readonly int _depthStride;
    private readonly int _colorStride;
    private readonly bool _isPaletted;

    internal VidRun(Point position, int count, ReadOnlyMemory<byte> depths, int depthStride, ReadOnlyMemory<byte> colors, int colorStride, bool isPaletted)
    {
        Position = position;
        Count = count;
        _depths = depths;
        _depthStride = depthStride;
        _colors = colors;
        _colorStride = colorStride;
        _isPaletted = isPaletted;
    }

    /// <summary>
    /// Gets the canvas position of the first pixel of the run.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the number of pixels.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Returns the depth of a pixel.
    /// </summary>
    /// <param name="index">The index of the pixel in the run.</param>
    /// <returns>The depth of the pixel, or zero if the sprite doesn't have a depth buffer.</returns>
    public short GetDepth(int index) => _depths.IsEmpty ? (short)0 : MemoryMarshal.Read<short>(_depths.Span[(_depthStride * index)..]);

    /// <summary>
    /// Returns the color of a pixel: a palette index for paletted sprites, a 16-bit color otherwise.
    /// </summary>
    /// <param name="index">The index of the pixel in the run.</param>
    /// <returns>The color of the pixel.</returns>
    public ushort GetColor(int index) => _isPaletted ? _colors.Span[_colorStride * index] : MemoryMarshal.Read<ushort>(_colors.Span[(_colorStride * index)..]);

    /// <summary>
    /// Returns the 32-bit color of a pixel, for sprites that store one 32-bit color per pixel.
    /// </summary>
    /// <param name="index">The index of the pixel in the run.</param>
    /// <returns>The 32-bit color of the pixel.</returns>
    public uint GetColor32(int index) => MemoryMarshal.Read<uint>(_colors.Span[(_colorStride * index)..]);
}

using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

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

    internal VidRun(int x, int y, int count, ReadOnlyMemory<byte> depths, int depthStride, ReadOnlyMemory<byte> colors, int colorStride, bool isPaletted)
    {
        X = x;
        Y = y;
        Count = count;
        _depths = depths;
        _depthStride = depthStride;
        _colors = colors;
        _colorStride = colorStride;
        _isPaletted = isPaletted;
    }

    /// <summary>
    /// Gets the column of the first pixel in canvas coordinates.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the row in canvas coordinates.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Gets the number of pixels.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Returns the depth of a pixel, or zero for sprites without a depth buffer.
    /// </summary>
    public ushort GetDepth(int index) => _depths.IsEmpty ? (ushort)0 : MemoryMarshal.Read<ushort>(_depths.Span[(_depthStride * index)..]);

    /// <summary>
    /// Returns the color of a pixel: a palette index for paletted sprites, a 16-bit color otherwise.
    /// </summary>
    public ushort GetColor(int index) => _isPaletted ? _colors.Span[_colorStride * index] : MemoryMarshal.Read<ushort>(_colors.Span[(_colorStride * index)..]);

    /// <summary>
    /// Returns the 32-bit color of a pixel, for sprites that store one 32-bit color per pixel.
    /// </summary>
    public uint GetColor32(int index) => MemoryMarshal.Read<uint>(_colors.Span[(_colorStride * index)..]);
}

using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents one texture of a hardware sprite, an entry of its SURF chunk.
/// </summary>
/// <param name="Size">The size in pixels.</param>
/// <param name="Data">The pixel bytes row by row: 16-bit values, or palette indexes for a texture with a palette.</param>
/// <param name="Palette">The 256 entries of red, green and blue bytes of a texture whose pixels are palette indexes, or an empty value.</param>
public readonly record struct VidTexture(Size Size, ReadOnlyMemory<byte> Data, ReadOnlyMemory<byte> Palette = default)
{
    /// <summary>
    /// Gets the ARGB4444 pixels row by row.
    /// </summary>
    public ReadOnlySpan<ushort> Pixels => MemoryMarshal.Cast<byte, ushort>(Data.Span);
}

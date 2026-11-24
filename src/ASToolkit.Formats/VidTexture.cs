using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents one texture of a hardware sprite, an entry of its SURF chunk.
/// </summary>
/// <param name="Size">The size in pixels.</param>
/// <param name="Data">The pixel bytes row by row, as 16-bit values or, for a texture with a palette, as palette indexes.</param>
/// <param name="Palette">The 256 entries of red, green and blue bytes of a texture whose pixels are palette indexes, or an empty value.</param>
/// <param name="StreamSize">The byte count the texture's stream declares.</param>
/// <param name="DecodedSize">The byte count read or decoded from the texture's stream, equal to <paramref name="StreamSize"/> when the stream was read.</param>
/// <param name="Failure">Why the texture's stream couldn't be read, or <see cref="VidSurfaceFailure.None"/> when it was.</param>
public readonly record struct VidTexture(Size Size, ReadOnlyMemory<byte> Data, ReadOnlyMemory<byte> Palette = default, int StreamSize = 0, int DecodedSize = 0, VidSurfaceFailure Failure = VidSurfaceFailure.None)
{
    /// <summary>
    /// Gets the ARGB4444 pixels row by row.
    /// </summary>
    public ReadOnlySpan<ushort> Pixels => MemoryMarshal.Cast<byte, ushort>(Data.Span);
}

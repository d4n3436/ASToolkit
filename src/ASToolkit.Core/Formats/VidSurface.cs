using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one texture of a sprite with surfaces.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Data">The pixel bytes row by row.</param>
public readonly record struct VidSurface(int Width, int Height, ReadOnlyMemory<byte> Data)
{
    /// <summary>
    /// Gets the ARGB4444 pixels row by row.
    /// </summary>
    public ReadOnlySpan<ushort> Pixels => MemoryMarshal.Cast<byte, ushort>(Data.Span);
}

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the opaque pixels of one sprite frame, in canvas coordinates.
/// </summary>
/// <param name="Xs">The column of each pixel.</param>
/// <param name="Ys">The row of each pixel.</param>
/// <param name="Depths">The per-pixel depth of each pixel, or zeros for sprites without a depth buffer.</param>
public sealed record VidFrame(int[] Xs, int[] Ys, int[] Depths)
{
    /// <summary>
    /// Gets the topmost row with a pixel.
    /// </summary>
    public int Top => Ys.Length == 0 ? 0 : Ys.Min();

    /// <summary>
    /// Gets the number of rows from the topmost to the lowest pixel.
    /// </summary>
    public int Rows => Ys.Length == 0 ? 0 : Ys.Max() - Top + 1;
}

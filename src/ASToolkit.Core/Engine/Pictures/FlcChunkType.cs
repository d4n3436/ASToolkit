namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies the type of a chunk in a frame of an FLC animation file.
/// </summary>
public enum FlcChunkType : byte
{
    /// <summary>
    /// The chunk sets palette entries from 8-bit red, green and blue values.
    /// </summary>
    Color256 = 4,

    /// <summary>
    /// The chunk changes lines of pixels with word-oriented runs.
    /// </summary>
    DeltaFlc = 7,

    /// <summary>
    /// The chunk sets palette entries from 6-bit red, green and blue values.
    /// </summary>
    Color64 = 11,

    /// <summary>
    /// The chunk changes lines of pixels with byte-oriented runs.
    /// </summary>
    DeltaFli = 12,

    /// <summary>
    /// The chunk clears all pixels.
    /// </summary>
    Black = 13,

    /// <summary>
    /// The chunk replaces all pixels with run-length encoded lines.
    /// </summary>
    ByteRun = 15,

    /// <summary>
    /// The chunk replaces all pixels with uncompressed lines.
    /// </summary>
    Copy = 16
}

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a RES chunk whose records can change.
/// </summary>
public sealed class ResChunkBuilder
{
    /// <summary>
    /// Gets the four-character chunk tag.
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets or sets the header flags. Without bit 31 the flags word is the record count.
    /// </summary>
    public ResChunkFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes the compression of the chunk's records saved, or 0 when they are stored uncompressed.
    /// </summary>
    public int CompressionSavings { get; set; }

    /// <summary>
    /// Gets or sets the records.
    /// </summary>
    public List<byte[]> Records { get; set; } = [];
}

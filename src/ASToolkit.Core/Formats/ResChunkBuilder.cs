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
    public uint Flags { get; set; }

    /// <summary>
    /// Gets or sets the header value that follows the flags when bit 31 is set.
    /// </summary>
    public uint Res { get; set; }

    /// <summary>
    /// Gets or sets the records.
    /// </summary>
    public List<byte[]> Records { get; set; } = [];
}

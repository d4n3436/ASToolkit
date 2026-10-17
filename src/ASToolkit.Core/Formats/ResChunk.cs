namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one chunk of a <see cref="ResFile"/>.
/// </summary>
public sealed class ResChunk
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

    /// <summary>
    /// Gets or sets the bytes after the last record, or the whole body of an unparsed chunk.
    /// </summary>
    public byte[] Tail { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether the body did not parse as records and is kept whole in <see cref="Tail"/>.
    /// </summary>
    public bool IsUnparsed { get; init; }
}

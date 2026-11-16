namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the flags of a resource chunk's body.
/// </summary>
[Flags]
public enum ResChunkFlags : uint
{
    /// <summary>
    /// No flags, the flags word holding the record count.
    /// </summary>
    None = 0,

    /// <summary>
    /// An extended header, with the compression savings and the record count after the flags.
    /// </summary>
    ExtendedHeader = 1u << 31
}

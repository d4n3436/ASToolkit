using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the start of a frame record of a sprite file with runs, followed by its extra data and then its rows.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 2)]
public readonly struct VidFrameHeader
{
    /// <summary>
    /// Represents the size of one entry of extra data, in bytes.
    /// </summary>
    public const int ExtraSize = 6;

    /// <summary>
    /// The number of entries of extra data.
    /// </summary>
    [FieldOffset(0)]
    public readonly short ExtraCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidFrameHeader"/> structure.
    /// </summary>
    /// <param name="extraCount">The number of entries of extra data.</param>
    public VidFrameHeader(short extraCount)
    {
        ExtraCount = extraCount;
    }

    /// <summary>
    /// Gets the size of this header and its extra data in bytes, the offset where the rows start.
    /// </summary>
    public int Size => sizeof(short) + ExtraCount * ExtraSize;
}

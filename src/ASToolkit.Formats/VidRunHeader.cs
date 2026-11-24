using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of a run of pixels in a row of a frame record, or the end of the row when both fields are zero.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 2)]
public readonly struct VidRunHeader
{
    /// <summary>
    /// The number of pixels skipped before the run.
    /// </summary>
    [FieldOffset(0)]
    public readonly byte Skip;

    /// <summary>
    /// The number of pixels in the run.
    /// </summary>
    [FieldOffset(1)]
    public readonly byte Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidRunHeader"/> structure.
    /// </summary>
    /// <param name="skip">The number of pixels skipped before the run.</param>
    /// <param name="count">The number of pixels in the run.</param>
    public VidRunHeader(byte skip, byte count)
    {
        Skip = skip;
        Count = count;
    }

    /// <summary>
    /// Gets a value indicating whether this header ends the row.
    /// </summary>
    public bool IsEnd => Skip == 0 && Count == 0;
}

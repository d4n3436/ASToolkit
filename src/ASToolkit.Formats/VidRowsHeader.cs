using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of the rows of a frame record, followed by the runs of each row.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 4)]
public readonly struct VidRowsHeader
{
    /// <summary>
    /// The first row, relative to the top of the frame.
    /// </summary>
    [FieldOffset(0)]
    public readonly short First;

    /// <summary>
    /// The number of rows.
    /// </summary>
    [FieldOffset(2)]
    public readonly short Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="VidRowsHeader"/> structure.
    /// </summary>
    /// <param name="first">The first row, relative to the top of the frame.</param>
    /// <param name="count">The number of rows.</param>
    public VidRowsHeader(short first, short count)
    {
        First = first;
        Count = count;
    }
}

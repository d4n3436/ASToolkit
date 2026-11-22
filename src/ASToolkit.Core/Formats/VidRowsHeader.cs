using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the header of the rows of a frame record, which the runs of each row follow.
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
}

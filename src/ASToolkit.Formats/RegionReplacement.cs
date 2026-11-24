using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a pair of object IDs a region sprite stores, the object ID it changes and the object ID it changes it to.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct RegionReplacement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegionReplacement"/> structure.
    /// </summary>
    /// <param name="from">The object ID the region changes.</param>
    /// <param name="to">The object ID it changes to.</param>
    public RegionReplacement(int from, int to)
    {
        From = from;
        To = to;
    }

    /// <summary>
    /// The object ID the region changes.
    /// </summary>
    [FieldOffset(0)]
    public readonly int From;

    /// <summary>
    /// The object ID it changes to.
    /// </summary>
    [FieldOffset(4)]
    public readonly int To;
}

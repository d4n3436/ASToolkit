namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the groups of a map's <c>GROU</c> chunk, each as a span of sprite handles over the chunk's bytes.
/// </summary>
public ref struct MapGroupEnumerator
{
    private const int GroupEnd = -1;
    private readonly ReadOnlySpan<int> _handles;
    private int _start;

    internal MapGroupEnumerator(ReadOnlySpan<int> handles)
    {
        _handles = handles;
    }

    /// <summary>
    /// Gets the sprite handles of the current group.
    /// </summary>
    public ReadOnlySpan<int> Current { get; private set; }

    /// <summary>
    /// Returns this enumerator.
    /// </summary>
    /// <returns>This enumerator.</returns>
    public readonly MapGroupEnumerator GetEnumerator() => this;

    /// <summary>
    /// Advances to the next group.
    /// </summary>
    /// <returns><see langword="true"/> if another group was read; <see langword="false"/> at the empty group that ends the record.</returns>
    public bool MoveNext()
    {
        int length = _handles[_start..].IndexOf(GroupEnd);

        if (length <= 0)
            return false;

        Current = _handles.Slice(_start, length);
        _start += length + 1;

        return true;
    }

    /// <summary>
    /// Copies the remaining groups into arrays.
    /// </summary>
    /// <returns>The sprite handles of each remaining group.</returns>
    public readonly int[][] ToArray()
    {
        int count = 0;
        foreach (var _ in this)
        {
            count++;
        }

        int[][] groups = new int[count][];
        int index = 0;
        foreach (var group in this)
        {
            groups[index++] = group.ToArray();
        }

        return groups;
    }
}

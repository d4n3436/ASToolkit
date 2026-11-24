namespace ASToolkit.Engine.IO;

/// <summary>
/// Represents a growable array of named values, looked up by name from the last entry back.
/// </summary>
/// <typeparam name="T">The type of the values.</typeparam>
public sealed class NamedList<T> : MyList<NamedListEntry<T>>
{
    /// <summary>
    /// Gets the value of the entry at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    public new ref T this[int index] => ref base[index].Value;

    /// <summary>
    /// Returns the name of the entry at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The name.</returns>
    public ReadOnlyMemory<byte> Name(int index) => base[index].Name;

    /// <summary>
    /// Returns the index of the last entry with a name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The index, or -1 if no entry has the name.</returns>
    public int Location(ReadOnlySpan<byte> name)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            if (base[i].Name.Span.SequenceEqual(name))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Adds an entry after the last one.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The index of the entry.</returns>
    [EngineRoutine(0x00424a20)]
    public int Insert(ReadOnlyMemory<byte> name, T value)
    {
        Insert(new NamedListEntry<T>(name, value));

        return Count - 1;
    }

    internal ref NamedListEntry<T> Entry(int index) => ref base[index];
}

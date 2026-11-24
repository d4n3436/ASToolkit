namespace ASToolkit.Engine.IO;

/// <summary>
/// Represents an entry of a <see cref="NamedList{T}"/>, a name and its value.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public struct NamedListEntry<T>
{
    /// <summary>
    /// The name of the entry.
    /// </summary>
    public ReadOnlyMemory<byte> Name;

    /// <summary>
    /// The value of the entry.
    /// </summary>
    public T Value;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedListEntry{T}"/> struct.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public NamedListEntry(ReadOnlyMemory<byte> name, T value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>
    /// Deconstructs the entry into its name and value.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public readonly void Deconstruct(out ReadOnlyMemory<byte> name, out T value)
    {
        name = Name;
        value = Value;
    }
}

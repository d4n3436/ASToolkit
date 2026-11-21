namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Represents a name of a compiled script.
/// </summary>
public struct ScriptObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptObject"/> class as a name of a compiled script.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="kind">The kind of the name.</param>
    /// <param name="address">The first slot, built-in ID or code offset.</param>
    /// <param name="firstVariable">The slot of the first parameter.</param>
    /// <param name="count">The number of parameters or elements.</param>
    public ScriptObject(ReadOnlyMemory<byte> name, ScriptObjectKind kind, int address, int firstVariable = 0, int count = 0)
    {
        Name = name;
        Kind = kind;
        Address = address;
        FirstVariable = firstVariable;
        Count = count;
    }

    /// <summary>
    /// Gets or sets the name of the object.
    /// </summary>
    public ReadOnlyMemory<byte> Name { get; set; }

    /// <summary>
    /// Gets or sets the kind of the name, such as a variable, an engine function or a script function.
    /// </summary>
    public ScriptObjectKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the text of a text constant.
    /// </summary>
    public ReadOnlyMemory<byte> Text { get; set; }

    /// <summary>
    /// Gets or sets the first slot of a variable, the ID of a built-in function or the code offset of a function or label.
    /// </summary>
    public int Address { get; set; }

    /// <summary>
    /// Gets or sets the slot of the first parameter of a function.
    /// </summary>
    public int FirstVariable { get; set; }

    /// <summary>
    /// Gets or sets the number of parameters of a function or of elements of a variable.
    /// </summary>
    public int Count { get; set; }
}

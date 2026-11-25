using System.Runtime.InteropServices;

namespace ASToolkit.Engine.Scripts;

/// <summary>
/// Represents the fixed fields stored after each global name of a compiled script or a demo's script state.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
public readonly struct ScriptNameRecord
{
    /// <summary>
    /// The kind of the name.
    /// </summary>
    [FieldOffset(0)]
    public readonly ScriptObjectKind Kind;

    /// <summary>
    /// The first slot of a variable, the ID of a built-in function or the code offset of a function.
    /// </summary>
    [FieldOffset(8)]
    public readonly int Address;

    /// <summary>
    /// The slot of the first parameter of a function.
    /// </summary>
    [FieldOffset(12)]
    public readonly int FirstVariable;

    /// <summary>
    /// The number of parameters of a function or of elements of a variable.
    /// </summary>
    [FieldOffset(16)]
    public readonly int Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptNameRecord"/> structure with the stored fields of a name.
    /// </summary>
    /// <param name="kind">The kind of the name.</param>
    /// <param name="address">The first slot, built-in ID or code offset.</param>
    /// <param name="firstVariable">The slot of the first parameter.</param>
    /// <param name="count">The number of parameters or elements.</param>
    public ScriptNameRecord(ScriptObjectKind kind, int address, int firstVariable, int count)
    {
        Kind = kind;
        Address = address;
        FirstVariable = firstVariable;
        Count = count;
    }
}

using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one entry of the action stack a placed sprite carries in its payload.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct MapAction
{
    /// <summary>
    /// The action.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Act;

    /// <summary>
    /// The first argument.
    /// </summary>
    [FieldOffset(4)]
    public readonly int Var1;

    /// <summary>
    /// The second argument.
    /// </summary>
    [FieldOffset(8)]
    public readonly int Var2;

    /// <summary>
    /// The third argument.
    /// </summary>
    [FieldOffset(12)]
    public readonly int Var3;
}

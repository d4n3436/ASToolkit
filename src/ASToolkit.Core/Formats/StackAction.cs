using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one entry of the action stack a placed sprite carries in its payload.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct StackAction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StackAction"/> struct.
    /// </summary>
    /// <param name="act">The action.</param>
    /// <param name="var1">The first argument.</param>
    /// <param name="var2">The second argument.</param>
    /// <param name="var3">The third argument.</param>
    public StackAction(int act, int var1, int var2, int var3)
    {
        Act = act;
        Var1 = var1;
        Var2 = var2;
        Var3 = var3;
    }

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

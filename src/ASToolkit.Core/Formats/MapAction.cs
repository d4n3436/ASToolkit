using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one entry of the action stack a placed sprite carries in its payload.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct MapAction
{
    /// <summary>
    /// The action code.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Code;

    /// <summary>
    /// The first argument.
    /// </summary>
    [FieldOffset(4)]
    public readonly int First;

    /// <summary>
    /// The second argument.
    /// </summary>
    [FieldOffset(8)]
    public readonly int Second;

    /// <summary>
    /// The third argument.
    /// </summary>
    [FieldOffset(12)]
    public readonly int Third;
}

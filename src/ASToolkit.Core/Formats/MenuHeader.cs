using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the header of a menu.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
public readonly struct MenuHeader
{
    /// <summary>
    /// The version of the menu.
    /// </summary>
    [FieldOffset(0)]
    public readonly int Version;

    /// <summary>
    /// The screen size the menu was laid out on.
    /// </summary>
    [FieldOffset(4)]
    public readonly Size Size;

    /// <summary>
    /// The origin of the menu's coordinates.
    /// </summary>
    [FieldOffset(12)]
    public readonly Point Origin;
}

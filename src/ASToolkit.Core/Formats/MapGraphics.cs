using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the graphics record of a map, which holds its ambient light.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
public readonly struct MapGraphics
{
    /// <summary>
    /// The environment the map starts with.
    /// </summary>
    [FieldOffset(0)]
    public readonly MapEnvironment Environment;

    /// <summary>
    /// The gamma of the ambient light.
    /// </summary>
    [FieldOffset(4)]
    public readonly Gamma Ambient;

    /// <summary>
    /// The angle of the direction vector.
    /// </summary>
    [FieldOffset(12)]
    public readonly uint Direction;

    /// <summary>
    /// The length of the direction vector.
    /// </summary>
    [FieldOffset(16)]
    public readonly float DirectionLength;
}

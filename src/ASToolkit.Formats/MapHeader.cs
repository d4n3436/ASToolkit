using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the header of a map.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public readonly struct MapHeader
{
    /// <summary>
    /// Represents the first version that stores sizes and positions as floats.
    /// </summary>
    public const int FloatVersion = 10;

    /// <summary>
    /// The size of the map in a file of version 10 or later.
    /// </summary>
    [FieldOffset(0)]
    public readonly SizeF FloatMapSize;

    /// <summary>
    /// The size of the map in a file older than version 10.
    /// </summary>
    [FieldOffset(0)]
    public readonly Size IntegerMapSize;

    /// <summary>
    /// The camera position in a file of version 10 or later.
    /// </summary>
    [FieldOffset(8)]
    public readonly Vector2 FloatCamera;

    /// <summary>
    /// The camera position in a file older than version 10.
    /// </summary>
    [FieldOffset(8)]
    public readonly Point IntegerCamera;

    /// <summary>
    /// The tick value.
    /// </summary>
    [FieldOffset(16)]
    public readonly int Tick;

    /// <summary>
    /// The map version.
    /// </summary>
    [FieldOffset(20)]
    public readonly int Version;

    /// <summary>
    /// Gets the map width in pixels.
    /// </summary>
    public float MapW => Version < FloatVersion ? IntegerMapSize.Width : FloatMapSize.Width;

    /// <summary>
    /// Gets the map height in pixels.
    /// </summary>
    public float MapH => Version < FloatVersion ? IntegerMapSize.Height : FloatMapSize.Height;

    /// <summary>
    /// Gets the starting position of the camera.
    /// </summary>
    public Vector2 Camera => Version < FloatVersion ? new Vector2(IntegerCamera.X, IntegerCamera.Y) : FloatCamera;
}

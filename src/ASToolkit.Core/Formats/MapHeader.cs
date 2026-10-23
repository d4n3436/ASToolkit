using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the header of a map.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public readonly struct MapHeader
{
    /// <summary>
    /// The map width in pixels.
    /// </summary>
    [FieldOffset(0)]
    public readonly float MapW;

    /// <summary>
    /// The map height in pixels.
    /// </summary>
    [FieldOffset(4)]
    public readonly float MapH;

    /// <summary>
    /// The starting position of the camera.
    /// </summary>
    [FieldOffset(8)]
    public readonly Vector2 Camera;

    /// <summary>
    /// The tick value.
    /// </summary>
    [FieldOffset(16)]
    public readonly uint Tick;

    /// <summary>
    /// The map version.
    /// </summary>
    [FieldOffset(20)]
    public readonly uint Version;
}

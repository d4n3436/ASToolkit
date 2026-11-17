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
    /// Represents the first version that stores sizes and positions as floats.
    /// </summary>
    public const int FloatVersion = 10;

    [FieldOffset(0)]
    private readonly float _mapW;

    [FieldOffset(0)]
    private readonly int _intMapW;

    [FieldOffset(4)]
    private readonly float _mapH;

    [FieldOffset(4)]
    private readonly int _intMapH;

    [FieldOffset(8)]
    private readonly Vector2 _camera;

    [FieldOffset(8)]
    private readonly int _intCameraX;

    [FieldOffset(12)]
    private readonly int _intCameraY;

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
    public float MapW => Version < FloatVersion ? _intMapW : _mapW;

    /// <summary>
    /// Gets the map height in pixels.
    /// </summary>
    public float MapH => Version < FloatVersion ? _intMapH : _mapH;

    /// <summary>
    /// Gets the starting position of the camera.
    /// </summary>
    public Vector2 Camera => Version < FloatVersion ? new Vector2(_intCameraX, _intCameraY) : _camera;
}

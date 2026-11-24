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
    /// The game time the map was saved at.
    /// </summary>
    [FieldOffset(16)]
    public readonly int Tick;

    /// <summary>
    /// The map version.
    /// </summary>
    [FieldOffset(20)]
    public readonly int Version;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapHeader"/> structure with integer coordinates, for a version below <see cref="FloatVersion"/>.
    /// </summary>
    /// <param name="mapSize">The size of the map.</param>
    /// <param name="camera">The camera position.</param>
    /// <param name="tick">The game time the map was saved at.</param>
    /// <param name="version">The map version.</param>
    public MapHeader(Size mapSize, Point camera, int tick, int version)
    {
        IntegerMapSize = mapSize;
        IntegerCamera = camera;
        Tick = tick;
        Version = version;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapHeader"/> structure with float coordinates, for version <see cref="FloatVersion"/> and later.
    /// </summary>
    /// <param name="mapSize">The size of the map.</param>
    /// <param name="camera">The camera position.</param>
    /// <param name="tick">The game time the map was saved at.</param>
    /// <param name="version">The map version.</param>
    public MapHeader(SizeF mapSize, Vector2 camera, int tick, int version)
    {
        FloatMapSize = mapSize;
        FloatCamera = camera;
        Tick = tick;
        Version = version;
    }

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

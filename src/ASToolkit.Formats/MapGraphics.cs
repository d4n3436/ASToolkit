using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents the graphics record of a map that stores its environment and ambient light.
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
    public readonly Angle Direction;

    /// <summary>
    /// The length of the direction vector.
    /// </summary>
    [FieldOffset(16)]
    public readonly float DirectionLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapGraphics"/> structure.
    /// </summary>
    /// <param name="environment">The environment the map starts with.</param>
    /// <param name="ambient">The gamma of the ambient light.</param>
    /// <param name="direction">The angle of the direction vector.</param>
    /// <param name="directionLength">The length of the direction vector.</param>
    public MapGraphics(MapEnvironment environment, Gamma ambient, Angle direction, float directionLength)
    {
        Environment = environment;
        Ambient = ambient;
        Direction = direction;
        DirectionLength = directionLength;
    }
}

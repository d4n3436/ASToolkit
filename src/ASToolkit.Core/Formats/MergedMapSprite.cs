using System.Numerics;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents one placed object of a map with its payload.
/// </summary>
/// <param name="Handle">The sprite handle that links the entry to its payload.</param>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Position">The position in map pixels, with the height above the floor.</param>
/// <param name="Direction">The direction of the object.</param>
/// <param name="Army">The army of the object.</param>
/// <param name="Payload">The object's payload bytes.</param>
/// <param name="Gamma">The gamma of the placed object.</param>
public sealed record MergedMapSprite(uint Handle, KnownObject Id, Vector3 Position, Angle Direction, Army Army, byte[] Payload,
    Gamma Gamma = default)
{
    /// <summary>
    /// Gets the scale of the sprite along x, y and the height.
    /// </summary>
    public Vector3 Scale { get; init; } = Vector3.One;
}

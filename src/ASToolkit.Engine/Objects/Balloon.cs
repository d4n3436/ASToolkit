using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a balloon sprite.
/// </summary>
public sealed class Balloon : Plane
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Balloon"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    // TODO: Port the balloon move tact (0x44e6f0)
    [EngineRoutine(0x0044dd80)]
    public Balloon(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
    }
}

using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a civilian robot sprite.
/// </summary>
public sealed class CivRobot : Creature
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CivRobot"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044a590)]
    public CivRobot(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
    }
    
    // NOTE: CivRobot code left unported
}

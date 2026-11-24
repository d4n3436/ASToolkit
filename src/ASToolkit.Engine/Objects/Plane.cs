using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a flying unit sprite.
/// </summary>
public class Plane : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plane"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044ce60)]
    public Plane(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        StartMove();
        VerticalSpeed = Vid.ZSpeed;
    }

    [EngineRoutine(0x0044ceb0)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target) =>
        base.Action(act, var1, var2, var3, target);
}

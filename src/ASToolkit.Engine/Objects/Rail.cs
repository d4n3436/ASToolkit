using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a rail sprite.
/// </summary>
public sealed class Rail : Terrain
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Rail"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Rail(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target)
    {
        // TODO: Port the Rail destructor (0x44a1a0) and action (0x44a240)
        return map.SpriteAction(this, act, var1, var2, var3, target);
    }
}

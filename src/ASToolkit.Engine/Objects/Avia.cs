using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a flying unit sprite.
/// </summary>
public class Avia : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Avia"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Avia(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, definition, position, army, hitPoints, map, direction)
    {
    }

    [EngineRoutine(0x0044ceb0)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target) =>
        base.Action(act, var1, var2, var3, target);
}

using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a building sprite.
/// </summary>
public sealed class Building : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Building"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Building(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.BuildingAction(this, act, var1, var2, var3, target);

    internal override void MoveTact(Map map)
    {
        // TODO: Port the call of 0x44be50 that 0x44ca50 makes for object 104
    }
}

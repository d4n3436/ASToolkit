using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a creature sprite.
/// </summary>
public class Creature : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Creature"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Creature(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    internal bool IsSeekingRegion { get; set; }

    internal Region? Region { get; set; }

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.CreatureAction(this, act, var1, var2, var3, target);

    internal override void MoveTact(Map map)
    {
        // TODO: Port the creature move tact (0x44d480)
        map.Effects.RunGeneric(this, map.FrameTime);
    }

    internal override void DeletePointerToSprite(Map map, Sprite removed) => map.Motion.ForgetCreatureRegion(this, removed);
}

using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a train depot sprite.
/// </summary>
public sealed class Depo : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Depo"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Depo(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    internal override void Destruct(Map map)
    {
        // TODO: Port the Depo destructor (0x44c0d0), action (0x44c130) and move tact (0x44c100)
        map.DestructSprite(this);
    }

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.SpriteAction(this, act, var1, var2, var3, target);

    internal override void MoveTact(Map map) => map.Effects.RunGeneric(this, map.FrameTime);
}

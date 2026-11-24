using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a cannon sprite, a fired projectile.
/// </summary>
public sealed class Cannon : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Cannon"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Cannon(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the cannon climbs onto higher ground.
    /// </summary>
    public bool IsClimbing { get; set; }

    internal override int Action(Map map, int act, int var1, int var2, int var3, Sprite? target) => map.CannonAction(this, act, var1, var2, var3, target);

    internal override void MoveTact(Map map) => map.Effects.RunCannon(this, map.FrameTime);

    internal override void DeletePointerToSprite(Map map, Sprite removed) => map.Motion.ForgetCannonGoal(this, removed);
}

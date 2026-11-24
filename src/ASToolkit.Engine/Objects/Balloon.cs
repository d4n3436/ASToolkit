using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a balloon sprite.
/// </summary>
public sealed class Balloon : Avia
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Balloon"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Balloon(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    internal override void MoveTact(Map map)
    {
        // TODO: Port the balloon move tact (0x44e6f0)
        map.Effects.RunGeneric(this, map.FrameTime);
    }
}

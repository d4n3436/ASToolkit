using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a train engine sprite.
/// </summary>
public sealed class Engine : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Engine"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Engine(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, definition, position, army, hitPoints, map, direction)
    {
    }
}

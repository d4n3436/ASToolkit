using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a primitive sprite.
/// </summary>
public sealed class Primitive : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Primitive"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Primitive(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    internal override void MoveTact(Map map)
    {
    }

    internal override void DeletePointerToSprite(Map map, Sprite removed)
    {
    }

    internal override void DrawInfo(Map map)
    {
    }
}

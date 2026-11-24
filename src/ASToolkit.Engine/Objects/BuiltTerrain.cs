using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a built terrain sprite that is drawn into the textures of the hardware ground when the map has one.
/// </summary>
public sealed class BuiltTerrain : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltTerrain"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public BuiltTerrain(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    /// <summary>
    /// Draws the sprite unless the hardware ground holds the terrain.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x0040eed0)]
    public override void Draw(Screen screen, Map map, Vector2 camera)
    {
        var ground = map.HardwareGroundDefinition;
        if (ground != map.Definitions.Empty && ground.Directions == 1)
            return;

        base.Draw(screen, map, camera);
    }
}

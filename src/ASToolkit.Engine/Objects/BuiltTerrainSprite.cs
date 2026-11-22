using System.Numerics;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a built terrain sprite, which is drawn into the textures of the hardware ground when the map has one.
/// </summary>
public sealed class BuiltTerrainSprite : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltTerrainSprite"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="parent">The sprite that created this sprite.</param>
    public BuiltTerrainSprite(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null, Sprite? parent = null)
        : base(number, definition, position, army, hitPoints, direction, map, parent)
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
        if (ground != map.Definitions.Empty && ground.Layout.Directions == 1)
            return;

        base.Draw(screen, map, camera);
    }
}

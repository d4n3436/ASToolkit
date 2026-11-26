using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a built terrain sprite that is drawn into the textures of the hardware ground when the map has one.
/// </summary>
public sealed class BuiltTerrain : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltTerrain"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0040edf0)]
    public BuiltTerrain(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        if (!Map.HasHardwareGround)
        {
            Map.CreateEmptyHardwareGround();
        }

        Map.HardwareGroundVid.AddVidToTextures(this);

        if (Map.IsBaked(Vid))
        {
            ChangeAnimation(SpriteAnimation.Death);
        }
    }

    /// <summary>
    /// Draws the sprite unless the hardware ground holds the terrain.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    [EngineRoutine(0x0040eed0)]
    public override void Draw(Screen screen)
    {
        var ground = Map.HardwareGroundVid;
        if (ground != Map.EmptyVid && ground.Directions == 1)
            return;

        base.Draw(screen);
    }
}

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
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Primitive(int number, Vid vid, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, vid, position, army, hitPoints, map, direction)
    {
    }

    [EngineRoutine(0x0040ec50)]
    [EngineRoutine(0x00446070)]
    internal override void Tact()
    {
        if ((uint)Vid.GetVisualFrameDuration(Animation) <= (uint)(Map.Time - LastTickTime))
        {
            int frame = Frame + 1;
            LastTickTime = Map.Time;
            Frame = frame;

            if (LastFrame < frame)
            {
                Frame = FirstFrame;
            }
        }
    }

    internal override void MoveTact()
    {
    }

    internal override void DeletePointerToSprite(Sprite removed)
    {
    }

    internal override void DrawSecondaryInfo()
    {
    }
}

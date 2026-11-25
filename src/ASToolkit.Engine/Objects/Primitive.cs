using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a primitive sprite.
/// </summary>
public sealed class Primitive : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Primitive"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0040ec00)]
    public Primitive(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
    }

    [EngineRoutine(0x0040ec50)]
    [EngineRoutine(0x00446070)]
    internal override void Tact()
    {
        if ((uint)Vid.GetVisualFrameDuration(Animation) <= (uint)(Map.CurrentTime - LastTickTime))
        {
            int frame = Frame + 1;
            LastTickTime = Map.CurrentTime;
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

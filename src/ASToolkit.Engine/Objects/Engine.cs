using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a train engine sprite.
/// </summary>
public sealed class Engine : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Engine"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044e8b0)]
    public Engine(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
    }

    internal bool IsDepoBatchEnd { get; set; }

    internal int DepoBatch { get; set; }
    
    // TODO: Port Engine and Rail class tails (0x44e8b0, 0x44a140); the port builds those classes with their base constructors
    // TODO: Port slots (Engine destructor 0x44e9d0, action 0x44f320, move tact 0x44ec20, forget 0x44ed10, info 0x44ed90 and goal lines 0x44f0f0; Rail destructor 0x44a1a0 and action 0x44a240)
}

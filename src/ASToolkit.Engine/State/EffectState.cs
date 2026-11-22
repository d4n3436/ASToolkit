using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the state of a blood, debris, death, or scenery effect in play.
/// </summary>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Position">The map position and height of the effect.</param>
/// <param name="Direction">The direction used to draw the effect.</param>
/// <param name="Animation">The animation state of the effect.</param>
/// <param name="StartedAt">The time the effect was created, in milliseconds.</param>
/// <param name="Frame">The index of the frame the effect shows within its sprite.</param>
/// <param name="Keyframe">The position of the effect in its keyframe animation, in keyframes.</param>
public readonly record struct EffectState(KnownObject Id, Vector3 Position, Angle Direction, SpriteAnimation Animation, int StartedAt, int Frame,
    float Keyframe = 0);

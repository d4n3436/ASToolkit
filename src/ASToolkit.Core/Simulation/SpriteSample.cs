using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents how a recorded object of a playthrough looks from a time on, its position moving on in a straight line from the sample before.
/// </summary>
/// <param name="Time">The time of the sample, in milliseconds.</param>
/// <param name="Id">The object ID in objects.res.</param>
/// <param name="Position">The map position and height of the object.</param>
/// <param name="Frame">The index of the frame the object shows within its sprite.</param>
/// <param name="Facing">The facing of the object.</param>
/// <param name="Animation">The animation state of the object.</param>
/// <param name="Keyframe">The position of the object in its keyframe animation, in keyframes.</param>
public readonly record struct SpriteSample(int Time, KnownObject Id, Vector3 Position, int Frame, byte Facing, SpriteAnimation Animation, float Keyframe) : ITimed
{
    /// <summary>
    /// Returns how an object looks at a time from its last two samples, the last sample with its position and keyframe carried on at their pace.
    /// </summary>
    /// <param name="before">The sample before the last one.</param>
    /// <param name="last">The last sample at or before the time.</param>
    /// <param name="time">The time, in milliseconds.</param>
    public static SpriteSample Predict(in SpriteSample before, in SpriteSample last, int time)
    {
        int span = last.Time - before.Time;

        if (span <= 0 || time <= last.Time)
            return last;

        float share = (time - last.Time) / (float)span;

        return last with { Position = last.Position + (last.Position - before.Position) * share, Keyframe = last.Keyframe + (last.Keyframe - before.Keyframe) * share };
    }
}

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents one object of a recorded playthrough from its appearance to its removal, with its samples.
/// </summary>
/// <param name="Start">The time the object appears, in milliseconds.</param>
/// <param name="End">The time the object is gone, in milliseconds, or <see cref="int.MaxValue"/> when it lasts to the end.</param>
/// <param name="FirstSample">The index of the track's first sample in the replay's samples.</param>
/// <param name="SampleCount">The number of samples of the track.</param>
/// <param name="Owner">The track of the object this one is linked to, whose position its samples are relative to, or -1 when its samples hold map positions.</param>
/// <param name="LagsOwner">A value indicating whether the object takes its owner's position from the tick before.</param>
public readonly record struct SpriteTrack(int Start, int End, int FirstSample, int SampleCount, int Owner, bool LagsOwner) : ITimed
{
    int ITimed.Time => Start;
}

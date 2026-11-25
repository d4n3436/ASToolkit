namespace ASToolkit.Engine;

/// <summary>
/// Represents the state of a sound buffer that is playing.
/// </summary>
/// <param name="Sound">The sound whose sample the buffer holds.</param>
/// <param name="Variant">The sample of the sound, from 0 to 7.</param>
/// <param name="IsLooping">A value indicating whether the buffer plays its sample again when it ends.</param>
/// <param name="StartedAt">The real time, in milliseconds, at which the buffer was at the start of its sample.</param>
/// <param name="Volume">The volume in hundredths of a decibel, 0 at full loudness.</param>
/// <param name="Pan">The balance from -10000 (left) to 10000 (right), in hundredths of a decibel taken from the other side.</param>
public readonly record struct SoundBufferState(int Sound, int Variant, bool IsLooping, uint StartedAt, int Volume, int Pan);

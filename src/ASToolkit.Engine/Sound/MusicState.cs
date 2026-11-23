namespace ASToolkit.Engine;

/// <summary>
/// Represents the state of the music track that is playing.
/// </summary>
/// <param name="Path">The path of the track in the game folder.</param>
/// <param name="StartedAt">The wall time, in milliseconds, at which the track started.</param>
/// <param name="Volume">The volume in hundredths of a decibel, 0 at full loudness.</param>
public readonly record struct MusicState(string Path, uint StartedAt, int Volume);

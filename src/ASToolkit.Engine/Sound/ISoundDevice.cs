namespace ASToolkit.Engine;

/// <summary>
/// Defines a sound device that the sound system asks whether a voice or the music is playing.
/// </summary>
public interface ISoundDevice
{
    /// <summary>
    /// Determines whether the device is playing a voice.
    /// </summary>
    /// <param name="voice">The voice, from 0 to 15.</param>
    /// <returns><see langword="true"/> while the voice plays, <see langword="false"/> once it has ended, or <see langword="null"/> to time it from the sample's length.</returns>
    bool? IsVoicePlaying(int voice);

    /// <summary>
    /// Determines whether the device is playing the music.
    /// </summary>
    /// <returns><see langword="true"/> while the music plays, <see langword="false"/> once it has ended, or <see langword="null"/> to time it from the track's length.</returns>
    bool? IsMusicPlaying();
}

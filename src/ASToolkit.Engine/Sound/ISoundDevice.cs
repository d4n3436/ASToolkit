using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Defines the device that plays sounds and music.
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

    /// <summary>
    /// Sets the pan of a voice's buffer.
    /// </summary>
    /// <param name="voice">The voice, from 0 to 15.</param>
    /// <param name="pan">The balance from -10000 (left) to 10000 (right).</param>
    void SetVoicePan(int voice, int pan)
    {
    }

    /// <summary>
    /// Sets the volume of a voice's buffer.
    /// </summary>
    /// <param name="voice">The voice, from 0 to 15.</param>
    /// <param name="volume">The volume in hundredths of a decibel, 0 at full loudness.</param>
    void SetVoiceVolume(int voice, int volume)
    {
    }

    /// <summary>
    /// Starts the buffer of a voice from its current position.
    /// </summary>
    /// <param name="voice">The voice, from 0 to 15.</param>
    /// <param name="isLooping"><see langword="true"/> to play the buffer again when it ends.</param>
    /// <returns><see langword="true"/> if the buffer is lost; otherwise, <see langword="false"/>.</returns>
    bool PlayVoice(int voice, bool isLooping) => false;

    /// <summary>
    /// Sends a command string to the music device.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="window">The window handle to notify when the command finishes, or 0 for none.</param>
    /// <returns>The error code of the device, or 0 if the command succeeded.</returns>
    int SendMusicCommand(ReadOnlySpan<byte> command, nint window = 0) => 0;

    /// <summary>
    /// Stops the buffer of a voice, keeping its position.
    /// </summary>
    /// <param name="voice">The voice, from 0 to 15.</param>
    void StopVoice(int voice)
    {
    }

    /// <summary>
    /// Creates the sound device.
    /// </summary>
    /// <returns>A negative value if the device could not be created; otherwise, a value of 0 or more.</returns>
    int CreateDevice() => 0;

    /// <summary>
    /// Sets the cooperative level of the sound device for the game window.
    /// </summary>
    /// <param name="level">1 for normal, 2 for priority.</param>
    /// <returns>A negative value if the level could not be set; otherwise, a value of 0 or more.</returns>
    int SetCooperativeLevel(int level) => 0;

    /// <summary>
    /// Creates the primary buffer of the sound device.
    /// </summary>
    /// <returns>A negative value if the buffer could not be created; otherwise, a value of 0 or more.</returns>
    int CreatePrimaryBuffer() => 0;

    /// <summary>
    /// Plays the primary buffer in a loop.
    /// </summary>
    /// <returns>A negative value if the buffer could not play; otherwise, a value of 0 or more.</returns>
    int PlayPrimaryBuffer() => 0;

    /// <summary>
    /// Sets the format of the primary buffer to stereo 16-bit PCM at 44100 Hz.
    /// </summary>
    /// <returns>A negative value if the format could not be set; otherwise, a value of 0 or more.</returns>
    int SetPrimaryFormat() => 0;

    /// <summary>
    /// Releases the primary buffer.
    /// </summary>
    void ReleasePrimaryBuffer()
    {
    }

    /// <summary>
    /// Releases the sound device.
    /// </summary>
    /// <returns>The remaining reference count, 0 once the device is gone.</returns>
    int ReleaseDevice() => 0;

    /// <summary>
    /// Gets the play cursor of the music buffer.
    /// </summary>
    /// <returns>The offset in bytes into the buffer, from 0 to 262143, or <see langword="null"/> to time it from the start of the buffer.</returns>
    int? GetMusicPlayCursor() => null;

    /// <summary>
    /// Creates the buffer of a sample.
    /// </summary>
    /// <param name="sound">The sound.</param>
    /// <param name="variant">The sample of the sound, from 0 to 7.</param>
    /// <param name="format">The format of the sample.</param>
    /// <param name="size">The size of the buffer in bytes.</param>
    /// <param name="flags">The buffer capabilities, 0xc2 for a wav sample (pan, volume and static) and 0x10082 for a decoded Ogg sample (pan, volume and precise position).</param>
    /// <returns>A negative value if the buffer could not be created; otherwise, a value of 0 or more.</returns>
    int CreateSampleBuffer(int sound, int variant, in WaveFormat format, int size, int flags) => 0;

    /// <summary>
    /// Copies the samples of a wav file into the buffer of a sample.
    /// </summary>
    /// <param name="sound">The sound.</param>
    /// <param name="variant">The sample of the sound, from 0 to 7.</param>
    /// <param name="samples">The bytes of the data chunk.</param>
    void FillWaveSampleBuffer(int sound, int variant, ReadOnlySpan<byte> samples)
    {
    }

    /// <summary>
    /// Decodes the next part of an Ogg Vorbis sample and copies it into the buffer of the sample.
    /// </summary>
    /// <param name="sound">The sound.</param>
    /// <param name="variant">The sample of the sound, from 0 to 7.</param>
    /// <param name="file">The bytes of the file.</param>
    /// <param name="offset">The offset in the buffer to copy to, in bytes.</param>
    /// <param name="count">The number of bytes to decode at most.</param>
    /// <returns>The number of bytes decoded, 0 at the end of the file, a negative decoder error code, or <see langword="null"/> to decode from the size of the sample.</returns>
    int? ReadSampleChunk(int sound, int variant, ReadOnlyMemory<byte> file, int offset, int count) => null;

    /// <summary>
    /// Decodes the next part of the music track into the ring buffer.
    /// </summary>
    /// <param name="count">The number of bytes to decode at most.</param>
    /// <returns>The number of bytes decoded, 0 at the end of the track, a negative decoder error code, or <see langword="null"/> to decode from the length of the track.</returns>
    int? ReadMusicChunk(int count) => null;
}

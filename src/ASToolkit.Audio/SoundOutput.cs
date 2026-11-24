using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine;
using ASToolkit.Formats;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Audio;

/// <summary>
/// Plays the sound effects and music of a game session on the default audio device.
/// </summary>
/// <remarks>
/// The output plays whatever the session's sound state is playing. It plays nothing if no audio device or SDL library is available.
/// </remarks>
public sealed unsafe class SoundOutput : IDisposable
{
    private const int SampleRate = 44100;

    private readonly Lock _syncGate = new();
    private readonly Lock _mixGate = new();
    private readonly string _gameFolder;
    private readonly SampleLibrary _samples;
    private readonly SoundMixer _mixer;
    private readonly SDL_AudioStream* _stream;
    private GCHandle _handle;
    private MusicTrack? _music;
    private string? _endedTrack;
    private uint _endedTrackStart;
    private float[] _mix = [];
    private int _generation;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundOutput"/> class and opens the default audio device.
    /// </summary>
    /// <param name="gameFolder">The game folder the samples and tracks are read from.</param>
    /// <param name="records">The sounds of the object table.</param>
    public SoundOutput(string gameFolder, IReadOnlyList<SfxRecord> records)
    {
        _gameFolder = gameFolder;
        _samples = new SampleLibrary(gameFolder, records, SampleRate);
        _mixer = new SoundMixer(SampleRate, _samples);

        try
        {
            if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
                return;
        }
        catch (DllNotFoundException)
        {
            return;
        }

        _handle = GCHandle.Alloc(this);
        var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = SoundMixer.Channels, freq = SampleRate };
        _stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, &OnAudioNeeded, GCHandle.ToIntPtr(_handle));

        if (_stream == null)
        {
            _handle.Free();
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);

            return;
        }

        SDL_ResumeAudioStreamDevice(_stream);
        IsAvailable = true;
    }

    /// <summary>
    /// Gets a value indicating whether an audio device is open.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Updates the output to match a session's sound state.
    /// </summary>
    /// <param name="sound">The session's sound state.</param>
    /// <param name="wallTime">The session's wall time in milliseconds.</param>
    /// <param name="isQuiet"><see langword="true"/> to resume only looping sounds and music, such as after a seek; <see langword="false"/> to also start sounds that play once.</param>
    /// <param name="generation">The playback number that <see cref="Silence"/> returned. Calls with an older number are ignored.</param>
    public void Sync(Sound sound, uint wallTime, bool isQuiet, int generation)
    {
        lock (_syncGate)
        {
            if (!IsAvailable || _isDisposed || generation != _generation)
                return;

            for (int buffer = 0; buffer < sound.BufferCount; buffer++)
            {
                if (sound.TryGetPlayingBuffer(buffer, out var state))
                {
                    _samples.TryGetSample(state.Sound, state.Variant, out _);
                }
            }

            bool hasMusic = sound.TryGetPlayingMusic(out var music);
            bool isCurrentMusic = hasMusic && _music is not null && _music.Path == music.Path && _music.StartedAt == music.StartedAt;
            var opened = hasMusic && !isCurrentMusic ? OpenMusic(music, wallTime) : null;
            MusicTrack? replaced = null;
            lock (_mixGate)
            {
                _mixer.BeginSync();
                for (int buffer = 0; buffer < sound.BufferCount; buffer++)
                {
                    if (sound.TryGetPlayingBuffer(buffer, out var state))
                    {
                        _mixer.Apply(buffer, state, wallTime, isQuiet);
                    }
                }

                _mixer.EndSync();

                if (!isCurrentMusic)
                {
                    replaced = _music;
                    _music = opened;
                }

                _music?.Gain = SoundMixer.GetGain(music.Volume);
            }

            replaced?.Dispose();
        }
    }

    /// <summary>
    /// Stops all sounds and music.
    /// </summary>
    /// <returns>A new playback number to pass to <see cref="Sync"/>.</returns>
    public int Silence()
    {
        lock (_syncGate)
        {
            _generation++;
            _endedTrack = null;
            MusicTrack? stopped;
            lock (_mixGate)
            {
                _mixer.Clear();
                stopped = _music;
                _music = null;
            }

            stopped?.Dispose();

            return _generation;
        }
    }

    /// <summary>
    /// Closes the audio device and releases the music.
    /// </summary>
    public void Dispose()
    {
        lock (_syncGate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (IsAvailable)
            {
                SDL_DestroyAudioStream(_stream);
            }

            _music?.Dispose();
            _music = null;

            if (IsAvailable)
            {
                SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
                _handle.Free();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAudioNeeded(nint userdata, SDL_AudioStream* stream, int additionalAmount, int totalAmount)
    {
        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is SoundOutput output)
            {
                output.Fill(stream, additionalAmount);
            }
        }
        catch (Exception)
        {
        }
    }

    private void Fill(SDL_AudioStream* stream, int bytes)
    {
        int count = bytes / (sizeof(float) * SoundMixer.Channels) * SoundMixer.Channels;

        if (count <= 0)
            return;

        if (_mix.Length < count)
        {
            _mix = new float[count];
        }

        var mix = _mix.AsSpan(0, count);
        mix.Clear();
        try
        {
            lock (_mixGate)
            {
                _mixer.Mix(mix);
                _music?.Mix(mix);
            }
        }
        catch (Exception)
        {
            mix.Clear();
        }

        fixed (float* samples = mix)
        {
            SDL_PutAudioStreamData(stream, (nint)samples, count * sizeof(float));
        }
    }

    private MusicTrack? OpenMusic(MusicState state, uint wallTime)
    {
        if (_endedTrack == state.Path && _endedTrackStart == state.StartedAt)
            return null;

        string? file = GamePath.Resolve(_gameFolder, state.Path);
        MusicTrack? opened;
        try
        {
            opened = file is null ? null : MusicTrack.Open(file, state.Path, state.StartedAt, wallTime, SampleRate);
        }
        catch (Exception)
        {
            opened = null;
        }

        if (opened is null)
        {
            _endedTrack = state.Path;
            _endedTrackStart = state.StartedAt;
        }

        return opened;
    }
}

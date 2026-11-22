using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the sound effects and music of a game session.
/// </summary>
public sealed class SoundSystem
{
    /// <summary>
    /// Represents the volume of a sound heard at full loudness, in hundredths of a decibel.
    /// </summary>
    public const int FullLoudness = 0;

    private const int SfxRecordSize = 72;
    private const int RequestCount = 32;
    private const int VoiceCount = 16;
    private const int NoSound = -1;
    private const int NoBuffer = -1;
    private const byte LoopedLimit = 0;
    private const byte GlobalLimit = (byte)'d';
    private const int VoicesPerLimitStep = 10;
    private const int UnlimitedVoices = 999999;
    private const int FullVolume = 100;
    private const int VolumeScale = 32;
    private const int QuietestVolume = -3000;
    private const int MaxPan = 10000;
    private const int PanScale = 4;
    private const int LoudnessScale = 2;
    private const int MergeReach = 100;
    private const int RepeatWindow = 50;
    private const int LoopHold = 100;
    private const int TrackCheckMask = 15;
    private const uint FadeWindow = 1000;
    private const int FadeStart = -1;
    private const int MixLatency = 10;
    private const int RingLead = 16;
    private const int PcmSampleSize = 2;
    private const int ExtensionLength = 4;
    private const float MillisecondsPerSecond = 1000;

    private readonly IGameFileSystem _fileSystem;
    private readonly ISoundDevice? _device;
    private readonly IReadOnlyList<SfxRecord> _records;
    private readonly int[] _variants;
    private readonly SoundBuffer[] _buffers;
    private readonly SoundBuffer[] _duplicates = new SoundBuffer[VoiceCount];
    private readonly SfxRequest[] _requests = new SfxRequest[RequestCount];
    private readonly SfxBuffer[] _voices = new SfxBuffer[VoiceCount];
    private readonly Dictionary<string, float> _trackLengths = [];
    private readonly bool _isDisabled;
    private int _soundVolume = FullVolume;
    private int _musicVolume = NoSound;
    private int _fade;
    private int _loop;
    private ReadOnlyMemory<byte> _trackName;
    private MusicStream? _music;
    private uint _fadeStartedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundSystem"/> class and loads the samples of every sound.
    /// </summary>
    /// <param name="records">The sounds of the object table.</param>
    /// <param name="fileSystem">The file system the samples and tracks are read from.</param>
    /// <param name="device">The device asked whether voices and music are playing, or <see langword="null"/> to time them from their lengths.</param>
    /// <param name="isDisabled"><see langword="true"/> for a session without a sound device, which loads nothing and ignores every request.</param>
    /// <param name="isHighQuality"><see langword="true"/> when the player chose high-quality sound, which sets the format of the primary buffer.</param>
    public SoundSystem(IReadOnlyList<SfxRecord> records, IGameFileSystem fileSystem, ISoundDevice? device = null, bool isDisabled = false, bool isHighQuality = false)
        : this(records, fileSystem, null, device, isDisabled, isHighQuality)
    {
    }

    [EngineRoutine(0x0041cba0)]
    internal SoundSystem(IReadOnlyList<SfxRecord> records, IGameFileSystem fileSystem, GameLogger? logger, ISoundDevice? device = null, bool isDisabled = false, bool isHighQuality = false)
    {
        Logger = logger;
        IsHighQuality = isHighQuality;
        _fileSystem = fileSystem;
        _device = device;
        _isDisabled = isDisabled;
        _records = records;
        _variants = new int[records.Count];
        _buffers = new SoundBuffer[records.Count * SfxRecord.MaxSamples];
        for (int i = 0; i < RequestCount; i++)
        {
            _requests[i].Id = NoSound;
        }

        for (int i = 0; i < VoiceCount; i++)
        {
            _voices[i] = new SfxBuffer(NoSound, NoBuffer);
        }

        if (!_isDisabled)
        {
            Load(records);
        }
    }

    internal GameLogger? Logger { get; set; }

    /// <summary>
    /// Gets a value indicating whether the player chose high-quality sound.
    /// </summary>
    public bool IsHighQuality { get; }

    private static ReadOnlySpan<byte> OggExtensions => ".Ogg.OGG.ogg"u8;

    /// <summary>
    /// Gets a value indicating whether a music track exists and plays or loops.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    /// <returns><see langword="true"/> while music plays; otherwise, <see langword="false"/>.</returns>
    public bool IsPlayMusic(uint wallTime) => _music is not null && (_music.IsPlaying(wallTime) || _loop != 0);

    /// <summary>
    /// Determines whether a sound loops, keeping one voice alive while it is requested.
    /// </summary>
    /// <param name="id">The sound.</param>
    /// <returns><see langword="true"/> for a loaded looping sound; otherwise, <see langword="false"/>.</returns>
    public bool IsLooped(int id) => IsValid(id) && _records[id].VoiceLimit == LoopedLimit;

    /// <summary>
    /// Requests a sound for the end of the frame.
    /// </summary>
    /// <param name="id">The sound.</param>
    /// <param name="pan">The balance from -10000 (left) to 10000 (right).</param>
    /// <param name="volume">The volume in hundredths of a decibel, 0 at full loudness.</param>
    [EngineRoutine(0x0041d600)]
    public void Play(int id, int pan, int volume)
    {
        if (_isDisabled || IsInvalid(id))
            return;

        int adjustment = (_soundVolume - FullVolume) * VolumeScale;
        int level = volume + adjustment;

        if (level < QuietestVolume)
            return;

        pan = Math.Clamp(pan, -MaxPan, MaxPan);

        if (_records[id].VoiceLimit == GlobalLimit)
        {
            level = adjustment;
        }

        var requests = _requests.AsSpan();
        for (int i = 0; i < requests.Length; i++)
        {
            if (requests[i].Id < 0)
            {
                requests[i] = new SfxRequest(id, pan, level);

                return;
            }
        }

        bool isRequested = false;
        int replaced = 0;
        for (int i = 1; i < requests.Length; i++)
        {
            if (requests[i].Id == id)
            {
                isRequested = true;
            }

            if (_records[requests[i].Id].VoiceLimit < _records[requests[replaced].Id].VoiceLimit)
            {
                replaced = i;
            }
        }

        if (replaced != 0 || !isRequested)
        {
            requests[replaced] = new SfxRequest(id, pan, level);
        }
    }

    /// <summary>
    /// Requests a sound heard from a point relative to the center of the screen.
    /// </summary>
    /// <param name="id">The sound.</param>
    /// <param name="offset">The distance from the center of the screen in pixels.</param>
    [EngineRoutine(0x0041d5b0)]
    public void PlayAt(int id, Vector2 offset)
    {
        int across = (int)offset.X;
        int down = (int)offset.Y;
        int acrossLevel = unchecked(-GetMagnitude(across) * LoudnessScale);
        int downLevel = unchecked(-GetMagnitude(down) * LoudnessScale);
        Play(id, unchecked(across * PanScale), Math.Min(acrossLevel, downLevel));
    }

    /// <summary>
    /// Advances the music and starts the sounds requested during the frame.
    /// </summary>
    /// <param name="random">The engine's random source.</param>
    /// <param name="time">The game time in milliseconds.</param>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041d770)]
    public void Tact(GameRandom random, int time, uint wallTime)
    {
        var music = _music;

        if (music is not null)
        {
            music.Update(wallTime);
            bool isNextTrack;

            if (_fade < 0)
            {
                if (_fadeStartedAt == 0 || wallTime - _fadeStartedAt > FadeWindow)
                {
                    _fadeStartedAt = wallTime;
                }

                music.SetVolume(_fade + (_musicVolume >= 0 ? (_musicVolume - FullVolume) * VolumeScale : 0));
                _fade -= (int)((wallTime - _fadeStartedAt) / 2);
                isNextTrack = _fade < QuietestVolume;

                if (isNextTrack)
                {
                    _fade = 0;
                    _fadeStartedAt = 0;
                }
            }
            else
            {
                isNextTrack = (random.Next() & TrackCheckMask) == 0 && !music.IsPlaying(wallTime);
            }

            if (isNextTrack)
            {
                if (_trackName.Length == 0)
                {
                    StopMusic(wallTime);
                }
                else
                {
                    StartMusic(_trackName, _loop, wallTime);
                }
            }
        }

        if (_isDisabled)
            return;

        var requests = _requests.AsSpan();
        var voices = _voices.AsSpan();
        int pending = 0;
        for (int r = 0; r < requests.Length; r++)
        {
            ref var request = ref requests[r];
            int id = request.Id;

            if (id < 0)
                continue;

            int playing = 0;
            for (int v = 0; v < voices.Length; v++)
            {
                ref var voice = ref voices[v];

                if (voice.Id != id || !voice.IsPlaying)
                    continue;

                if (IsLooped(id) && Math.Abs(voice.Volume - request.Volume) < MergeReach && Math.Abs(voice.Pan - request.Pan) < MergeReach)
                {
                    request.Id = NoSound;
                    PlayVoice(ref voice, request.Pan, request.Volume, time, wallTime);

                    break;
                }

                playing++;
                byte limit = _records[id].VoiceLimit;
                int voiceLimit = limit == GlobalLimit ? UnlimitedVoices : limit == LoopedLimit ? 1 : limit / VoicesPerLimitStep;

                if (voiceLimit < playing)
                {
                    request.Id = NoSound;

                    break;
                }
            }

            if (request.Id >= 0)
            {
                pending++;
            }
        }

        int free = 0;
        for (int v = 0; v < voices.Length; v++)
        {
            if (!GetVoiceStatus(ref voices[v], v, time, wallTime))
            {
                free++;
            }
        }

        for (; free < pending; pending--)
        {
            if (requests[0].Id < 0)
            {
                requests[0].Volume = 0;
            }

            bool isFound = false;
            int quietest = 0;
            for (int r = 0; r < requests.Length; r++)
            {
                if (requests[r].Id >= 0 && _records[requests[r].Id].VoiceLimit != GlobalLimit && requests[r].Volume <= requests[quietest].Volume)
                {
                    isFound = true;
                    quietest = r;
                }
            }

            if (!isFound)
                break;

            requests[quietest].Id = NoSound;
        }

        for (int v = 0; free < pending && v < voices.Length; v++)
        {
            ref var voice = ref voices[v];

            if (voice.IsPlaying && _records[voice.Id].VoiceLimit != GlobalLimit)
            {
                voice.IsPlaying = false;

                if (voice.Buffer != NoBuffer)
                {
                    GetBuffer(voice.Buffer).Stop(wallTime);
                }

                free++;
            }
        }

        for (int r = 0; r < requests.Length; r++)
        {
            ref var request = ref requests[r];

            if (request.Id < 0)
                continue;

            int picked = PickVoice(request.Id, random, time);

            if (picked >= 0)
            {
                PlayVoice(ref voices[picked], request.Pan, request.Volume, time, wallTime);
            }

            request.Id = NoSound;
        }
    }

    /// <summary>
    /// Stops the voices of a sound, or every voice.
    /// </summary>
    /// <param name="id">The sound, or -1 to stop every voice.</param>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041d560)]
    public void Stop(int id, uint wallTime)
    {
        if (_isDisabled)
            return;

        if (id == NoSound)
        {
            StopAll(wallTime);

            return;
        }

        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            if (voices[v].Id == id)
            {
                ReleaseVoice(ref voices[v]);
            }
        }
    }

    /// <summary>
    /// Sets the volume of later sound requests.
    /// </summary>
    /// <param name="volume">The volume from 0 to 100.</param>
    [EngineRoutine(0x0041ddc0)]
    public void SetVolume(int volume) => _soundVolume = volume < 0 ? 0 : Math.Min(volume, FullVolume);

    /// <summary>
    /// Plays a music track and fades the current one out first.
    /// </summary>
    /// <param name="name">The path of the track.</param>
    /// <param name="loop">A nonzero value to play the track again when it ends.</param>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041dec0)]
    public void PlayMusicFile(ReadOnlySpan<byte> name, int loop, uint wallTime)
    {
        if (_music is null)
        {
            StartMusic(name.ToArray(), loop, wallTime);

            return;
        }

        _fade = FadeStart;
        _trackName = name.ToArray();
        _loop = loop;
    }

    /// <summary>
    /// Stops the music and clears the remembered track to play again.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041dc70)]
    public void StopMusic(uint wallTime)
    {
        _music?.Stop(wallTime);
        _trackName = ReadOnlyMemory<byte>.Empty;
        _loop = 0;
    }

    /// <summary>
    /// Sets the volume of the music, removing the track at 0 and starting the remembered one when the volume is above 0.
    /// </summary>
    /// <param name="volume">The volume from 0 to 100.</param>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041ddf0)]
    public void SetMusicVolume(int volume, uint wallTime)
    {
        if (volume <= 0)
        {
            volume = 0;

            if (_music is not null)
            {
                DestroyMusic();
            }
        }
        else if (volume > FullVolume)
        {
            volume = FullVolume;
        }

        _musicVolume = volume;

        if (_music is null && volume != 0)
        {
            StartMusic(_trackName, _loop, wallTime);
        }

        _music?.SetVolume((volume - FullVolume) * VolumeScale);
    }

    private static int GetMagnitude(int value)
    {
        int sign = value >> 31;

        return unchecked((value ^ sign) - sign);
    }

    private static bool IsOgg(ReadOnlySpan<byte> path)
    {
        for (int i = 0; i < OggExtensions.Length; i += ExtensionLength)
        {
            if (path.IndexOf(OggExtensions.Slice(i, ExtensionLength)) >= 0)
                return true;
        }

        return false;
    }

    internal bool IsValid(int id) => id >= 0 && id < _variants.Length && _variants[id] > 0;

    private bool IsInvalid(int id)
    {
        if (IsValid(id))
            return false;

        if (_records.Count > 0)
        {
            Logger?.Error("SOUND"u8, LogError.Invalid, 0, default);
        }

        return true;
    }

    [EngineRoutine(0x0041d110)]
    private void Load(IReadOnlyList<SfxRecord> records)
    {
        int start = Environment.TickCount;
        for (int id = 0; id < records.Count; id++)
        {
            _variants[id] = LoadSfx(records[id], _buffers.AsSpan(id * SfxRecord.MaxSamples, SfxRecord.MaxSamples));
        }

        Logger?.Write("LoadSFX::No   =%-15i   sizeof(SFX)   =%-5i    load time     =%ims Quality=%i"u8, records.Count, SfxRecordSize, Environment.TickCount - start, IsHighQuality ? 1 : 0);
    }

    [EngineRoutine(0x0041e810)]
    private int LoadSfx(SfxRecord record, Span<SoundBuffer> buffers)
    {
        int variants = 0;
        for (int k = 0; k < SfxRecord.MaxSamples; k++)
        {
            var path = record.GetSamplePath(k);

            if (path.IsEmpty)
                break;

            float length = IsOgg(path) ? LoadOggLength(path) : LoadWaveLength(path);

            if (length < 0)
                break;

            buffers[k] = new SoundBuffer(length + MixLatency, 1);
            variants = k + 1;
        }

        return variants;
    }

    [EngineRoutine(0x0041e370)]
    private float LoadWaveLength(ReadOnlySpan<byte> path)
    {
        if (!_fileSystem.TryRead(GameTextEncoding.Cyrillic.GetString(path), out var data))
            return -1;

        var wave = new WaveResource(data);
        int bytesPerSecond = (int)wave.Format.SamplesPerSec * wave.Format.BlockAlign;

        return bytesPerSecond <= 0 ? -1 : wave.Samples.Length * MillisecondsPerSecond / bytesPerSecond;
    }

    [EngineRoutine(0x0041e1d0)]
    private float LoadOggLength(ReadOnlySpan<byte> path)
    {
        if (!_fileSystem.TryRead(GameTextEncoding.Cyrillic.GetString(path), out var data))
            return -1;

        var track = new VorbisResource(data);

        return track.SampleRate <= 0 ? -1 : track.SampleCount * MillisecondsPerSecond / track.SampleRate;
    }

    private ref SoundBuffer GetBuffer(int handle) => ref handle < _buffers.Length ? ref _buffers[handle] : ref _duplicates[handle - _buffers.Length];

    private void ReleaseBuffer(int handle)
    {
        if (handle < _buffers.Length)
        {
            _buffers[handle].References--;
        }
    }

    [EngineRoutine(0x0041d430)]
    private int PickVoice(int id, GameRandom random, int time)
    {
        if (_isDisabled || IsInvalid(id))
            return -1;

        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            if (voices[v].Id != id)
                continue;

            if ((uint)(time - voices[v].Start) <= RepeatWindow)
                return -1;

            if (_variants[id] == 1 && !voices[v].IsPlaying)
                return v;
        }

        int picked = 0;
        while (picked < voices.Length && voices[picked].Id >= 0)
        {
            picked++;
        }

        if (picked >= voices.Length)
        {
            picked = 0;
            while (voices[picked].IsPlaying)
            {
                picked++;

                if (picked >= voices.Length)
                    return -1;
            }

            ReleaseVoice(ref voices[picked]);
        }

        int buffer = CloneSample(id, picked, random);
        AssignVoice(ref voices[picked], id, buffer);

        return picked;
    }

    [EngineRoutine(0x0041eb30)]
    private int CloneSample(int id, int voice, GameRandom random)
    {
        int handle = id * SfxRecord.MaxSamples + random.Next() % _variants[id];
        ref var original = ref _buffers[handle];
        original.References++;

        if (original.References <= 2)
            return handle;

        original.References--;
        _duplicates[voice] = new SoundBuffer(original.Length, 1);

        return _buffers.Length + voice;
    }

    [EngineRoutine(0x0041e560)]
    private void PlayVoice(ref SfxBuffer voice, int pan, int volume, int time, uint wallTime)
    {
        if (voice.Id < 0 || voice.Buffer == NoBuffer)
            return;

        voice.Volume = volume;
        voice.Pan = pan;

        if (!voice.IsPlaying)
        {
            GetBuffer(voice.Buffer).Play(IsLooped(voice.Id), wallTime);
        }

        voice.IsPlaying = true;
        voice.Start = time;
    }

    [EngineRoutine(0x0041e6e0)]
    private bool GetVoiceStatus(ref SfxBuffer voice, int index, int time, uint wallTime)
    {
        if (voice.Id < 0 || voice.Buffer == NoBuffer || !voice.IsPlaying)
            return false;

        ref var buffer = ref GetBuffer(voice.Buffer);

        bool isTimedPlaying = buffer.GetStatus(wallTime);

        if (!(_device?.IsVoicePlaying(index) ?? isTimedPlaying))
        {
            voice.IsPlaying = false;

            if (isTimedPlaying)
            {
                buffer.Stop(wallTime);
            }
        }

        if (IsLooped(voice.Id) && (uint)(time - voice.Start) > LoopHold)
        {
            voice.IsPlaying = false;
            buffer.Stop(wallTime);
        }

        return voice.IsPlaying;
    }

    [EngineRoutine(0x0041e530)]
    private void ReleaseVoice(ref SfxBuffer voice)
    {
        if (voice.Buffer != NoBuffer)
        {
            ReleaseBuffer(voice.Buffer);
        }

        voice.Buffer = NoBuffer;
        voice.IsPlaying = false;
        voice.Start = 0;
        voice.Id = NoSound;
    }

    [EngineRoutine(0x0041e780)]
    private void AssignVoice(ref SfxBuffer voice, int id, int buffer)
    {
        voice.Id = id;

        if (voice.Buffer != NoBuffer)
        {
            ReleaseBuffer(voice.Buffer);
        }

        voice.Buffer = buffer;
        voice.IsPlaying = false;
        voice.Start = 0;
    }

    [EngineRoutine(0x0041dcd0)]
    private void StopAll(uint wallTime)
    {
        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            ref var voice = ref voices[v];
            voice.IsPlaying = false;

            if (voice.Buffer != NoBuffer)
            {
                GetBuffer(voice.Buffer).Stop(wallTime);
            }
        }
    }

    [EngineRoutine(0x0041df20)]
    private int StartMusic(ReadOnlyMemory<byte> name, int loop, uint wallTime)
    {
        if (name.Length == 0)
            return 1;

        _loop = loop;
        _fade = 0;
        _trackName = loop == 0 ? ReadOnlyMemory<byte>.Empty : name;

        if (_music is null || !name.Span.SequenceEqual(_music.Name.Span))
        {
            _music = null;

            if (_musicVolume == 0)
                return 0;

            _music = CreateMusic(name);
        }

        if (_musicVolume > 0)
        {
            _music.SetVolume((_musicVolume - FullVolume) * VolumeScale);
        }

        _music.Play(wallTime);

        return 0;
    }

    [EngineRoutine(0x0041c310)]
    private MusicStream CreateMusic(ReadOnlyMemory<byte> name)
    {
        // TODO: Port the DirectShow music class for tracks that are not ogg
        string path = GameTextEncoding.Cyrillic.GetString(name.Span);

        if (!_trackLengths.TryGetValue(path, out float length))
        {
            length = -1;

            if (_fileSystem.TryRead(path, out var data))
            {
                var track = new VorbisResource(data);
                int bytesPerSecond = track.SampleRate * track.Channels * PcmSampleSize;

                if (bytesPerSecond > 0)
                {
                    length = (RingLead + track.SampleCount * track.Channels * PcmSampleSize) * MillisecondsPerSecond / bytesPerSecond;
                }
            }

            _trackLengths[path] = length;
        }

        return new MusicStream(name, Math.Max(length, 0), length >= 0, _device);
    }

    [EngineRoutine(0x0041d0a0)]
    private void DestroyMusic()
    {
        _music = null;
        _fade = 0;
    }
}

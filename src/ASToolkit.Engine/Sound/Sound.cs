using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the sound effects and music of the game.
/// </summary>
public sealed class Sound
{
    /// <summary>
    /// Represents the volume of a sound heard at full loudness, in hundredths of a decibel.
    /// </summary>
    public const int FullLoudness = 0;
    internal const int OggReadSize = 0x1000;
    private const int SfxRecordSize = 72;
    private const int RequestCount = 32;
    private const int VoiceCount = 16;
    internal const int NoSound = -1;
    internal const int NoBuffer = -1;
    private const int PriorityLevel = 2;
    private const int NormalLevel = 1;
    private const int DeviceReady = 0;
    private const int DeviceReleased = 1;
    private const int DeviceCreateFailed = 2;
    private const int DeviceLevelFailed = 3;
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
    internal const int LoopHold = 100;
    private const int TrackCheckMask = 15;
    private const uint FadeWindow = 1000;
    private const int FadeStart = -1;
    internal const int MixLatency = 10;
    private const int PcmSampleSize = 2;
    private const int FormatMinimumSize = 14;
    private const int WaveBufferFlags = 0xc2;
    private const int OggBufferFlags = 0x10082;
    private const ushort OggFormatExtraSize = 0x12;
    private const int ExtensionLength = 4;
    private const float MillisecondsPerSecond = 1000;

    private readonly IGameFileSystem _fileSystem;
    private readonly ISoundDevice? _device;
    private Sfx[] _sfx = [];
    private readonly SoundBuffer[] _duplicates = new SoundBuffer[VoiceCount];
    private readonly SfxRequest[] _requests = new SfxRequest[RequestCount];
    private readonly SfxBuffer[] _voices = new SfxBuffer[VoiceCount];
    private readonly Dictionary<string, MusicTrackFormat> _trackFormats = [];
    private readonly nint _window;
    private int _soundVolume = FullVolume;
    private int _musicVolume = NoSound;
    private int _fade;
    private int _loop;
    private ReadOnlyMemory<byte> _trackName;
    private OggMusic? _music;
    private uint _fadeStartedAt;
    private int _deviceState = DeviceReleased;

    /// <summary>
    /// Creates the sound system with the sounds of the game's object resource and loads the samples of every sound.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the samples and tracks.</param>
    /// <param name="device">The sound device, or <see langword="null"/> to time voices and music from their lengths.</param>
    /// <param name="isDisabled"><see langword="true"/> to load nothing and ignore every request; otherwise, <see langword="false"/>.</param>
    /// <param name="isHighQuality"><see langword="true"/> for high-quality sound; otherwise, <see langword="false"/>.</param>
    /// <param name="window">The window handle to notify when a track ends.</param>
    /// <returns>The sound system.</returns>
    public static Sound Create(IGameFileSystem fileSystem, ISoundDevice? device = null, bool isDisabled = false, bool isHighQuality = false, nint window = 0)
    {
        var resource = new Resource();
        Map.OpenObjects(fileSystem, null, ref resource);

        return new Sound(ref resource, fileSystem, null, device, isDisabled, isHighQuality, window);
    }

    [EngineRoutine(0x0041cba0)]
    internal Sound(ref Resource resource, IGameFileSystem fileSystem, MyError? error, ISoundDevice? device = null, bool isDisabled = false, bool isHighQuality = false, nint window = 0)
    {
        _window = window;
        Error = error;
        IsHighQuality = isHighQuality;
        _fileSystem = fileSystem;
        _device = device;
        for (int i = 0; i < RequestCount; i++)
        {
            _requests[i].Id = NoSound;
        }

        for (int i = 0; i < VoiceCount; i++)
        {
            _voices[i] = new SfxBuffer(NoSound, NoBuffer);
        }

        if (!isDisabled)
        {
            InitializeDevice();
            LoadSfx(ref resource);
        }
    }

    /// <summary>
    /// Gets a value indicating whether sound is played in high quality.
    /// </summary>
    public bool IsHighQuality { get; private set; }

    /// <summary>
    /// Gets the number of sound buffers, including the duplicate buffer of each voice.
    /// </summary>
    public int BufferCount => SoundBufferCount + VoiceCount;

    internal MyError? Error { get; set; }

    internal ISoundDevice? Device => _device;

    internal bool IsLoaded => _deviceState == DeviceReady && _sfx.Length > 0;

    [EngineRoutine(0x0041d020)]
    internal int PlayingCount
    {
        get
        {
            if (_deviceState != DeviceReady)
                return 0;

            int count = 0;
            for (int i = VoiceCount - 1; i >= 0; i--)
            {
                if (_voices[i].IsPlaying)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private static ReadOnlySpan<byte> OggExtensions => ".Ogg.OGG.ogg"u8;

    private static ReadOnlySpan<byte> SfxTag => "SFX "u8;

    private int PlayToState { get; set; } = NoSound;

    private int PlayToPosition { get; set; } = NoSound;

    /// <summary>
    /// Determines whether a music track is playing or looping.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    /// <returns><see langword="true"/> while music plays; otherwise, <see langword="false"/>.</returns>
    public bool IsPlayMusic(uint wallTime) => _music is not null && (_music.IsPlaying(wallTime) || _loop != 0);

    /// <summary>
    /// Attempts to get the state of a sound buffer that is playing.
    /// </summary>
    /// <param name="buffer">The buffer, from 0 to <see cref="BufferCount"/> - 1.</param>
    /// <param name="state">When this method returns, contains the state of the buffer, if it is playing; otherwise, the default value.</param>
    /// <returns><see langword="true"/> if the buffer is playing; otherwise, <see langword="false"/>.</returns>
    public bool TryGetPlayingBuffer(int buffer, out SoundBufferState state)
    {
        state = default;

        if (buffer < 0 || buffer >= BufferCount)
            return false;

        ref var source = ref GetBuffer(buffer);

        if (!source.IsPlaying)
            return false;

        state = new SoundBufferState(source.Sound, source.Variant, source.IsLooping, source.PlayedAt, source.Volume, source.Pan);

        return true;
    }

    /// <summary>
    /// Attempts to get the state of the music track that is playing.
    /// </summary>
    /// <param name="state">When this method returns, contains the state of the track, if one is playing; otherwise, the default value.</param>
    /// <returns><see langword="true"/> if a track is playing; otherwise, <see langword="false"/>.</returns>
    public bool TryGetPlayingMusic(out MusicState state)
    {
        if (_music is null || !_music.IsBufferPlaying)
        {
            state = default;

            return false;
        }

        state = new MusicState(_music.Path, _music.PlayedAt, _music.Volume);

        return true;
    }

    /// <summary>
    /// Determines whether a sound loops and keeps one voice alive while it is requested.
    /// </summary>
    /// <param name="id">The sound.</param>
    /// <returns><see langword="true"/> for a loaded looping sound; otherwise, <see langword="false"/>.</returns>
    public bool IsLooped(int id) => IsValid(id) && _sfx[id].Record.VoiceLimit == LoopedLimit;

    /// <summary>
    /// Requests a sound for the end of the frame.
    /// </summary>
    /// <param name="id">The sound.</param>
    /// <param name="pan">The balance from -10000 (left) to 10000 (right).</param>
    /// <param name="volume">The volume in hundredths of a decibel, 0 at full loudness.</param>
    [EngineRoutine(0x0041d600)]
    public void Play(int id, int pan, int volume)
    {
        if (_deviceState != DeviceReady || IsInvalid(id))
            return;

        int adjustment = (_soundVolume - FullVolume) * VolumeScale;
        int level = volume + adjustment;

        if (level < QuietestVolume)
            return;

        pan = Math.Clamp(pan, -MaxPan, MaxPan);

        if (_sfx[id].Record.VoiceLimit == GlobalLimit)
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

            if (_sfx[requests[i].Id].Record.VoiceLimit < _sfx[requests[replaced].Id].Record.VoiceLimit)
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
    /// <param name="random">The random number generator.</param>
    /// <param name="time">The game time in milliseconds.</param>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041d770)]
    public void Tact(GameRandom random, int time, uint wallTime)
    {
        var music = _music;

        if (music is not null)
        {
            UpdateMusic(music, random, wallTime);
        }

        if (_deviceState != DeviceReady)
            return;

        var requests = _requests.AsSpan();
        var voices = _voices.AsSpan();
        int pending = MergeRequests(requests, voices, time, wallTime);
        int free = 0;
        for (int v = 0; v < voices.Length; v++)
        {
            if (!voices[v].CheckPlay(this, v, time, wallTime))
            {
                free++;
            }
        }

        pending = DropQuietestRequests(requests, free, pending);
        StopVoicesForRequests(voices, free, pending, wallTime);
        for (int r = 0; r < requests.Length; r++)
        {
            ref var request = ref requests[r];

            if (request.Id < 0)
                continue;

            int picked = PickVoice(request.Id, random, time);

            if (picked >= 0)
            {
                voices[picked].Play(this, picked, request.Pan, request.Volume, time, wallTime);
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
        if (_deviceState != DeviceReady)
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
                voices[v].Destroy(this);
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
    /// <param name="loop">Any nonzero value to play the track again when it ends.</param>
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
    /// <returns><see langword="true"/> if closing the music device failed; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0041dc70)]
    public bool StopMusic(uint wallTime)
    {
        _music?.Stop(wallTime);
        _trackName = default;
        _loop = 0;
        PlayToState = NoSound;
        _device?.SendMusicCommand("stop FWMUSIC"u8);

        return (_device?.SendMusicCommand("close FWMUSIC"u8) ?? 0) != 0;
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
        else
        {
            if (volume > FullVolume)
            {
                volume = FullVolume;
            }
        }

        _musicVolume = volume;

        if (_music is null && volume != 0)
        {
            StartMusic(_trackName, _loop, wallTime);
        }

        _music?.SetVolume((volume - FullVolume) * VolumeScale);
    }

    /// <summary>
    /// Releases the sound device.
    /// </summary>
    [EngineRoutine(0x0041d050)]
    public void DisableSound()
    {
        if (_deviceState != DeviceReady)
            return;

        ReleaseDevice();
    }

    /// <summary>
    /// Stops the music, releases the sound device and restores the volume of the auxiliary and wave output devices.
    /// </summary>
    [EngineRoutine(0x0041d080)]
    public void Disable()
    {
        DestroyMusic();
        DisableSound();
    }

    /// <summary>
    /// Pauses the music and every voice that plays.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041dd80)]
    public void Pause(uint wallTime)
    {
        PauseMusic(wallTime);
        PauseSound(wallTime);
    }

    /// <summary>
    /// Resumes the music and every voice that was playing.
    /// </summary>
    /// <param name="wallTime">The wall time in milliseconds.</param>
    [EngineRoutine(0x0041dda0)]
    public void Resume(uint wallTime)
    {
        ResumeMusic(wallTime);
        ResumeSound(wallTime);
    }

    internal bool IsValid(int id) => id >= 0 && id < _sfx.Length && _sfx[id].Count > 0;

    private static int GetMagnitude(int value)
    {
        int sign = value >> 31;

        return unchecked((value ^ sign) - sign);
    }

    internal static bool IsOgg(ReadOnlySpan<byte> path)
    {
        for (int i = 0; i < OggExtensions.Length; i += ExtensionLength)
        {
            if (path.IndexOf(OggExtensions.Slice(i, ExtensionLength)) >= 0)
                return true;
        }

        return false;
    }

    private bool IsInvalid(int id)
    {
        if (IsValid(id))
            return false;

        if (_sfx.Length > 0)
        {
            Error?.Error("SOUND"u8, LogError.Invalid, 0);
        }

        return true;
    }

    [EngineRoutine(0x0041d110)]
    private void LoadSfx(ref Resource resource)
    {
        int start = Environment.TickCount;
        if (_deviceState != DeviceReady)
            return;

        if (!resource.IsOpen)
        {
            Error?.Error("SOUND"u8, LogError.Open, 0, "res"u8);
            return;
        }

        int count = resource.CountRecords(SfxTag);
        if (count == 0)
        {
            Error?.Error("SOUND"u8, LogError.Section, 0, SfxTag);
            return;
        }

        _sfx = new Sfx[count];
        if (resource.GoBegin(SfxTag) != Resource.Found)
            return;

        int id = 0;
        do
        {
            _sfx[id] = new Sfx();
            _sfx[id].Load(this, id, new SfxRecord(resource.Record.ToArray()));
            id++;
        }
        while (resource.GoNextSub(SfxTag) == Resource.Found);

        Error?.Write($"LoadSFX::No   ={count,-15}   sizeof(SFX)   ={SfxRecordSize,-5}    load time     ={Environment.TickCount - start}ms Quality={(IsHighQuality ? 1 : 0)}");
    }

    [EngineRoutine(0x0041cd30)]
    private void InitializeDevice()
    {
        if (_deviceState == DeviceReady)
            return;

        int result = _device?.CreateDevice() ?? 0;

        if (result < 0)
        {
            _deviceState = DeviceCreateFailed;
            Error?.Error("SOUND"u8, LogError.Create, result, "DirectSound"u8);

            return;
        }

        if (IsHighQuality)
        {
            result = _device?.SetCooperativeLevel(PriorityLevel) ?? 0;

            if (result < 0)
            {
                IsHighQuality = false;
                Error?.Error("SOUND"u8, LogError.Set, result, "PriorityLevel"u8);
            }
        }

        if (!IsHighQuality)
        {
            result = _device?.SetCooperativeLevel(NormalLevel) ?? 0;
        }

        if (result < 0)
        {
            _device?.ReleaseDevice();
            _deviceState = DeviceLevelFailed;
            Error?.Error("SOUND"u8, LogError.Set, result, "CooperativeLevel"u8);

            return;
        }

        result = _device?.CreatePrimaryBuffer() ?? 0;

        if (result < 0)
        {
            Error?.Error("SOUND"u8, LogError.Create, result, "Primary sound buffer"u8);
        }
        else
        {
            result = _device?.PlayPrimaryBuffer() ?? 0;

            if (result < 0)
            {
                Error?.Error("SOUND"u8, LogError.Invalid, result, "unable play Primary"u8);
            }

            if (IsHighQuality)
            {
                result = _device?.SetPrimaryFormat() ?? 0;

                if (result < 0)
                {
                    Error?.Error("SOUND"u8, LogError.Set, result, "format primary buffer"u8);
                }
            }

            _device?.ReleasePrimaryBuffer();
        }

        _deviceState = DeviceReady;

        for (int id = 0; id < _sfx.Length; id++)
        {
            _sfx[id].Load(this, id, _sfx[id].Record);
        }
    }

    [EngineRoutine(0x0041cf70)]
    private void ReleaseDevice()
    {
        if (_deviceState != DeviceReady)
            return;

        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            voices[v].Destroy(this);
        }

        for (int id = 0; id < _sfx.Length; id++)
        {
            _sfx[id].Buffers.AsSpan().Clear();
        }

        int result = _device?.ReleaseDevice() ?? 0;

        if (result != 0)
        {
            Error?.Error("SOUND"u8, LogError.Text, result);
        }

        foreach (var sfx in _sfx)
        {
            sfx.Count = NoSound;
        }
        _deviceState = DeviceReleased;
    }

    [EngineRoutine(0x0041db80)]
    private bool PauseMusic(uint wallTime)
    {
        _music?.Pause(wallTime);

        return (_device?.SendMusicCommand("stop FWMUSIC"u8, _window) ?? 0) != 0;
    }

    [EngineRoutine(0x0041dbd0)]
    private bool ResumeMusic(uint wallTime)
    {
        _music?.Resume(wallTime);

        string command = PlayToState < 0 ? "play FWMUSIC notify" : $"play FWMUSIC to {PlayToPosition + 1} notify";

        return (_device?.SendMusicCommand(Encoding.ASCII.GetBytes(command), _window) ?? 0) != 0;
    }

    [EngineRoutine(0x0041dd10)]
    private void PauseSound(uint wallTime)
    {
        if (_deviceState != DeviceReady)
            return;

        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            ref var voice = ref voices[v];

            if (voice.IsPlaying && voice.Buffer != NoBuffer)
            {
                GetBuffer(voice.Buffer).Stop(wallTime);
                _device?.StopVoice(v);
            }
        }
    }

    [EngineRoutine(0x0041dd50)]
    private void ResumeSound(uint wallTime)
    {
        if (_deviceState != DeviceReady)
            return;

        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            voices[v].Resume(this, v, wallTime);
        }
    }

    [EngineRoutine(0x0041e190)]
    internal void ReloadBuffers()
    {
        for (int id = 0; id < _sfx.Length; id++)
        {
            _sfx[id].Load(this, id, _sfx[id].Record);
        }
    }

    [EngineRoutine(0x0041e370)]
    internal SoundBuffer LoadWave(ref Resource resource, ReadOnlySpan<byte> path, int sound, int variant)
    {
        if (_deviceState != DeviceReady)
            return default;

        if (resource.Open(_fileSystem, Error, path, Resource.AnyTag) != 0)
            return default;

        if (resource.GoBegin("fmt "u8) != 0)
        {
            Error?.Write($"!!!ERROR!!!SFX:'{MyError.Decode(path)}' 'fmt ' not found");

            return default;
        }

        if (resource.ChunkSize < FormatMinimumSize)
        {
            Error?.Write($"!!!ERROR!!!SFX:'{MyError.Decode(path)}' incorrect size {resource.ChunkSize}");

            return default;
        }

        Span<byte> header = stackalloc byte[Unsafe.SizeOf<WaveFormat>()];
        resource.Read(header);
        var format = MemoryMarshal.Read<WaveFormat>(header);

        if (resource.GoNext("data"u8) != 0 && resource.GoBegin("data"u8) != 0)
        {
            Error?.Write($"!!!ERROR!!!SFX:'{MyError.Decode(path)}' 'data' not found");

            return default;
        }

        int result = _device?.CreateSampleBuffer(sound, variant, format, resource.ChunkSize, WaveBufferFlags) ?? 0;

        if (result < 0)
        {
            Error?.Error("SOUND"u8, LogError.Create, result, "SoundBuffer"u8);

            return default;
        }

        int bytesPerSecond = (int)format.SamplesPerSec * format.BlockAlign;

        return new SoundBuffer(bytesPerSecond <= 0 ? 0 : resource.ChunkSize * MillisecondsPerSecond / bytesPerSecond, 1, sound, variant);
    }

    [EngineRoutine(0x0041e1d0)]
    internal SoundBuffer LoadOgg(ReadOnlySpan<byte> path, int sound, int variant, ref int size, out ReadOnlyMemory<byte> file)
    {
        file = default;

        if (_deviceState != DeviceReady)
            return default;

        if (path.IsEmpty || !_fileSystem.TryRead(Encoding.ASCII.GetString(path), out file))
        {
            Error?.Error("SOUND"u8, LogError.Open, 0, path);

            return default;
        }

        var track = new VorbisResource(file);

        if (track.SampleRate <= 0 || track.Channels <= 0)
        {
            Error?.Error("SOUND"u8, LogError.Invalid, 0, path);

            return default;
        }

        int blockAlign = track.Channels * PcmSampleSize;
        var format = new WaveFormat(1, (ushort)track.Channels, (uint)track.SampleRate, (uint)(track.SampleRate * blockAlign), (ushort)blockAlign, PcmSampleSize * 8, OggFormatExtraSize);

        if (size == 0)
        {
            size = (int)(track.SampleCount * blockAlign);
        }

        int result = _device?.CreateSampleBuffer(sound, variant, format, size, OggBufferFlags) ?? 0;

        if (result < 0)
        {
            Error?.Error("SOUND"u8, LogError.Create, result, "SoundBuffer for ogg"u8);

            return default;
        }

        return new SoundBuffer(track.SampleCount * MillisecondsPerSecond / track.SampleRate, 1, sound, variant);
    }

    internal int SoundBufferCount => _sfx.Length * SfxRecord.MaxSamples;

    internal Span<SoundBuffer> Duplicates => _duplicates;

    internal ref SoundBuffer GetBuffer(int handle) => ref handle < SoundBufferCount ? ref _sfx[handle / SfxRecord.MaxSamples].Buffers[handle % SfxRecord.MaxSamples] : ref _duplicates[handle - SoundBufferCount];

    internal bool IsDuplicate(int handle) => handle >= SoundBufferCount;

    internal void ReleaseBuffer(int handle)
    {
        if (IsDuplicate(handle))
        {
            _duplicates[handle - SoundBufferCount] = default;
        }
        else
        {
            GetBuffer(handle).References--;
        }
    }

    [EngineRoutine(0x0041d430)]
    private int PickVoice(int id, GameRandom random, int time)
    {
        if (_deviceState != DeviceReady || IsInvalid(id))
            return -1;

        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            if (voices[v].Id != id)
                continue;

            if ((uint)(time - voices[v].Start) <= RepeatWindow)
                return -1;

            if (_sfx[id].Count == 1 && !voices[v].IsPlaying)
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

            voices[picked].Destroy(this);
        }

        int buffer = _sfx[id].Duplicate(this, id, picked, random);
        voices[picked].Load(this, id, buffer);

        return picked;
    }

    [EngineRoutine(0x0041dcd0)]
    private void StopAll(uint wallTime)
    {
        var voices = _voices.AsSpan();
        for (int v = 0; v < voices.Length; v++)
        {
            StopVoice(ref voices[v], wallTime);
        }
    }

    [EngineRoutine(0x0041df20)]
    private int StartMusic(ReadOnlyMemory<byte> name, int loop, uint wallTime)
    {
        if (name.Length == 0)
            return 1;

        _loop = loop;
        _fade = 0;
        _trackName = loop == 0 ? default : name;

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
    private OggMusic CreateMusic(ReadOnlyMemory<byte> name)
    {
        // TODO: Port the DirectShow music class for tracks that are not ogg
        string path = Encoding.ASCII.GetString(name.Span);

        if (!_trackFormats.TryGetValue(path, out var format))
        {
            if (_fileSystem.TryRead(path, out var data))
            {
                var track = new VorbisResource(data);
                format = new MusicTrackFormat(track.SampleRate * track.Channels * PcmSampleSize, (int)(track.SampleCount * track.Channels * PcmSampleSize));
            }

            _trackFormats[path] = format;
        }

        return new OggMusic(name, path, format, _device, Error);
    }

    [EngineRoutine(0x0041d0a0)]
    private void DestroyMusic()
    {
        _music = null;
        _device?.SendMusicCommand("stop FWMUSIC"u8);
        _device?.SendMusicCommand("close FWMUSIC"u8);

        _fade = 0;
        PlayToPosition = NoSound;
    }

    private void UpdateMusic(OggMusic music, GameRandom random, uint wallTime)
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

    private int MergeRequests(Span<SfxRequest> requests, Span<SfxBuffer> voices, int time, uint wallTime)
    {
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
                    voice.Play(this, v, request.Pan, request.Volume, time, wallTime);

                    break;
                }

                playing++;
                byte limit = _sfx[id].Record.VoiceLimit;
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

        return pending;
    }

    private int DropQuietestRequests(Span<SfxRequest> requests, int free, int pending)
    {
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
                if (requests[r].Id >= 0 && _sfx[requests[r].Id].Record.VoiceLimit != GlobalLimit && requests[r].Volume <= requests[quietest].Volume)
                {
                    isFound = true;
                    quietest = r;
                }
            }

            if (!isFound)
                break;

            requests[quietest].Id = NoSound;
        }

        return pending;
    }

    private void StopVoicesForRequests(Span<SfxBuffer> voices, int free, int pending, uint wallTime)
    {
        for (int v = 0; free < pending && v < voices.Length; v++)
        {
            ref var voice = ref voices[v];

            if (voice.IsPlaying && _sfx[voice.Id].Record.VoiceLimit != GlobalLimit)
            {
                StopVoice(ref voice, wallTime);
                free++;
            }
        }
    }

    private void StopVoice(ref SfxBuffer voice, uint wallTime)
    {
        voice.IsPlaying = false;

        if (voice.Buffer != NoBuffer)
        {
            GetBuffer(voice.Buffer).Stop(wallTime);
        }
    }
}

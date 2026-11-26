using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

internal sealed class OggMusic
{
    private const int NoSample = -1;
    private const int RingSize = 0x40000;
    private const int RingMask = RingSize - 1;
    private const int ChunkSize = 0x1000;
    private const int RingLead = 16;
    private const int MillisecondsPerSecond = 1000;
    private const int Streaming = 0;
    private const int Ending = 1;
    private const int Wrapped = 2;

    private readonly ISoundDevice? _device;
    private readonly GameSession _session;
    private readonly MusicTrackFormat _format;
    private SoundBuffer _buffer;
    private bool _isPlaying;
    private int _decoded;
    private int _writeOffset;
    private int _endState;

    [EngineRoutine(0x0041c310)]
    public OggMusic(ReadOnlyMemory<byte> name, string path, MusicTrackFormat format, ISoundDevice? device, GameSession session)
    {
        _session = session;
        Name = name;
        Path = path;
        _device = device;
        _format = format;
        HasBuffer = format.BytesPerSecond > 0;
        _buffer = new SoundBuffer(HasBuffer ? (float)RingSize * MillisecondsPerSecond / format.BytesPerSecond : 0, 1, NoSample, NoSample);

        if (!HasBuffer)
        {
            session.Error.Error($"MUSIC '{MyError.Decode(name.Span)}'", LogError.Create, 0, "SoundBuffer"u8);
        }
    }

    public ReadOnlyMemory<byte> Name { get; }

    public string Path { get; }

    public bool IsBufferPlaying => HasBuffer && _buffer.IsPlaying;

    public uint PlayedAt => _buffer.PlayedAt;

    public bool HasBuffer { get; }

    public int Volume { get; private set; }

    [EngineRoutine(0x0041c4c0)]
    public void Play()
    {
        if (!HasBuffer)
            return;

        _buffer.Stop(_session.RealCurrentTime);
        _decoded = 0;
        _writeOffset = RingLead;
        _endState = Streaming;
        _buffer.Rewind();
        Update();
        _buffer.Play(true, _session.RealCurrentTime);
        _isPlaying = true;
    }

    [EngineRoutine(0x0041c540)]
    public void Stop()
    {
        _isPlaying = false;

        if (HasBuffer)
        {
            _buffer.Stop(_session.RealCurrentTime);
        }
    }

    [EngineRoutine(0x0041c560)]
    public void Pause()
    {
        if (_isPlaying && HasBuffer)
        {
            _buffer.Stop(_session.RealCurrentTime);
        }
    }

    [EngineRoutine(0x0041c580)]
    public void Resume()
    {
        if (_isPlaying && HasBuffer)
        {
            _buffer.Play(true, _session.RealCurrentTime);
        }
    }

    [EngineRoutine(0x0041c5a0)]
    public bool IsPlaying()
    {
        if (!HasBuffer || !_isPlaying)
            return false;

        bool isTimedPlaying = _buffer.GetStatus(_session.RealCurrentTime);

        if (!(_device?.IsMusicPlaying() ?? isTimedPlaying))
        {
            _isPlaying = false;

            if (isTimedPlaying)
            {
                _buffer.Stop(_session.RealCurrentTime);
            }
        }

        return _isPlaying;
    }

    [EngineRoutine(0x0041c5e0)]
    public void SetVolume(int volume) => Volume = volume;

    [EngineRoutine(0x0041c600)]
    public void Update()
    {
        if (!HasBuffer)
            return;

        int playCursor = _device?.GetMusicPlayCursor() ?? (int)(_buffer.GetPosition(_session.RealCurrentTime) * _format.BytesPerSecond / MillisecondsPerSecond % RingSize);

        while (_endState == Streaming)
        {
            int available = playCursor < _writeOffset ? playCursor - _writeOffset + RingSize : playCursor - _writeOffset;

            if (available < ChunkSize)
                break;

            int count = _device?.ReadMusicChunk(ChunkSize) ?? Math.Min(ChunkSize, _format.Size - _decoded);

            if (count == 0)
            {
                _endState = playCursor > _writeOffset ? Wrapped : Ending;
            }
            else if (count < 0)
            {
                _session.Error.Error($"MUSIC '{MyError.Decode(Name.Span)}'", LogError.Text, 0, "decode"u8);
            }
            else
            {
                _decoded += count;
                _writeOffset = _writeOffset + count & RingMask;
            }
        }

        if (_endState == Wrapped && playCursor < _writeOffset)
        {
            _endState = Ending;
        }

        if (_endState == Ending && playCursor >= _writeOffset)
        {
            Stop();
        }
    }
}

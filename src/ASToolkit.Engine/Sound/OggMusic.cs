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
    private readonly MyError? _error;
    private readonly MusicTrackFormat _format;
    private SoundBuffer _buffer;
    private bool _isPlaying;
    private int _decoded;
    private int _writeOffset;
    private int _endState;

    [EngineRoutine(0x0041c310)]
    public OggMusic(ReadOnlyMemory<byte> name, string path, MusicTrackFormat format, ISoundDevice? device, MyError? error)
    {
        _error = error;
        Name = name;
        Path = path;
        _device = device;
        _format = format;
        HasBuffer = format.BytesPerSecond > 0;
        _buffer = new SoundBuffer(HasBuffer ? (float)RingSize * MillisecondsPerSecond / format.BytesPerSecond : 0, 1, NoSample, NoSample);
    }

    public ReadOnlyMemory<byte> Name { get; }

    public string Path { get; }

    public bool IsBufferPlaying => HasBuffer && _buffer.IsPlaying;

    public uint PlayedAt => _buffer.PlayedAt;

    public bool HasBuffer { get; }

    public int Volume { get; private set; }

    [EngineRoutine(0x0041c4c0)]
    public void Play(uint realTime)
    {
        if (!HasBuffer)
            return;

        _buffer.Stop(realTime);
        _decoded = 0;
        _writeOffset = RingLead;
        _endState = Streaming;
        _buffer.Rewind();
        Update(realTime);
        _buffer.Play(true, realTime);
        _isPlaying = true;
    }

    [EngineRoutine(0x0041c540)]
    public void Stop(uint realTime)
    {
        _isPlaying = false;

        if (HasBuffer)
        {
            _buffer.Stop(realTime);
        }
    }

    [EngineRoutine(0x0041c560)]
    public void Pause(uint realTime)
    {
        if (_isPlaying && HasBuffer)
        {
            _buffer.Stop(realTime);
        }
    }

    [EngineRoutine(0x0041c580)]
    public void Resume(uint realTime)
    {
        if (_isPlaying && HasBuffer)
        {
            _buffer.Play(true, realTime);
        }
    }

    [EngineRoutine(0x0041c5a0)]
    public bool IsPlaying(uint realTime)
    {
        if (!HasBuffer || !_isPlaying)
            return false;

        bool isTimedPlaying = _buffer.GetStatus(realTime);

        if (!(_device?.IsMusicPlaying() ?? isTimedPlaying))
        {
            _isPlaying = false;

            if (isTimedPlaying)
            {
                _buffer.Stop(realTime);
            }
        }

        return _isPlaying;
    }

    [EngineRoutine(0x0041c5e0)]
    public void SetVolume(int volume) => Volume = volume;

    [EngineRoutine(0x0041c600)]
    public void Update(uint realTime)
    {
        if (!HasBuffer)
            return;

        int playCursor = _device?.GetMusicPlayCursor() ?? (int)(_buffer.GetPosition(realTime) * _format.BytesPerSecond / MillisecondsPerSecond % RingSize);

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
                _error?.Error($"MUSIC '{MyError.Decode(Name.Span)}'", LogError.Text, 0, "decode"u8);
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
            Stop(realTime);
        }
    }
}

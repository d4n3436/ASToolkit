namespace ASToolkit.Engine;

internal sealed class MusicStream
{
    private readonly ISoundDevice? _device;
    private SoundBuffer _buffer;
    private bool _isPlaying;

    public MusicStream(ReadOnlyMemory<byte> name, float length, bool hasBuffer, ISoundDevice? device)
    {
        Name = name;
        _device = device;
        _buffer = new SoundBuffer(length, 1);
        HasBuffer = hasBuffer;
    }

    public ReadOnlyMemory<byte> Name { get; }

    public bool HasBuffer { get; }

    public int Volume { get; private set; }

    [EngineRoutine(0x0041c4c0)]
    public void Play(uint wallTime)
    {
        if (!HasBuffer)
            return;

        _buffer.Stop(wallTime);
        _buffer.Rewind();
        Update(wallTime);
        _buffer.Play(true, wallTime);
        _isPlaying = true;
    }

    [EngineRoutine(0x0041c540)]
    public void Stop(uint wallTime)
    {
        _isPlaying = false;

        if (HasBuffer)
        {
            _buffer.Stop(wallTime);
        }
    }

    [EngineRoutine(0x0041c5a0)]
    public bool IsPlaying(uint wallTime)
    {
        if (!HasBuffer || !_isPlaying)
            return false;

        bool isTimedPlaying = _buffer.GetStatus(wallTime);

        if (!(_device?.IsMusicPlaying() ?? isTimedPlaying))
        {
            _isPlaying = false;

            if (isTimedPlaying)
            {
                _buffer.Stop(wallTime);
            }
        }

        return _isPlaying;
    }

    [EngineRoutine(0x0041c600)]
    public void Update(uint wallTime)
    {
        if (HasBuffer && _buffer.IsPlaying && wallTime - _buffer.PlayedAt >= _buffer.Length)
        {
            Stop(wallTime);
        }
    }

    [EngineRoutine(0x0041c5e0)]
    public void SetVolume(int volume) => Volume = volume;
}

namespace ASToolkit.Core.Engine;

internal sealed class MusicStream
{
    private SoundBuffer _buffer;
    private bool _isPlaying;

    public MusicStream(ReadOnlyMemory<byte> name, float length, bool hasBuffer)
    {
        Name = name;
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

        if (!_buffer.GetStatus(wallTime))
        {
            _isPlaying = false;
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

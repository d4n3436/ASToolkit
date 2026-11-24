namespace ASToolkit.Engine;

internal struct SoundBuffer
{
    public SoundBuffer(float length, int references, int sound, int variant)
    {
        Length = length;
        References = references;
        Sound = sound;
        Variant = variant;
        IsCreated = true;
    }

    public bool IsCreated { get; }

    public float Length { get; }

    public int Sound { get; }

    public int Variant { get; }

    public int Volume { get; set; }

    public int Pan { get; set; }

    public int References { get; set; }

    public int Position { get; private set; }

    public uint PlayedAt { get; private set; }

    public bool IsPlaying { get; private set; }

    public bool IsLooping { get; private set; }

    public void Play(bool isLooping, uint wallTime)
    {
        if (!GetStatus(wallTime))
        {
            PlayedAt = wallTime - (uint)Position;
            IsPlaying = true;
        }
        else if (IsLooping && !isLooping && Length > 0)
        {
            PlayedAt = wallTime - (uint)((wallTime - PlayedAt) % Length);
        }

        IsLooping = isLooping;
    }

    public void Rewind() => Position = 0;

    public uint GetPosition(uint wallTime) => IsPlaying ? wallTime - PlayedAt : (uint)Position;

    public void Stop(uint wallTime)
    {
        if (!GetStatus(wallTime))
            return;

        uint played = wallTime - PlayedAt;
        Position = IsLooping && Length > 0 ? (int)(played % Length) : (int)played;
        IsPlaying = false;
    }

    public bool GetStatus(uint wallTime)
    {
        if (IsPlaying && !IsLooping && wallTime - PlayedAt >= Length)
        {
            IsPlaying = false;
            Position = 0;
        }

        return IsPlaying;
    }
}

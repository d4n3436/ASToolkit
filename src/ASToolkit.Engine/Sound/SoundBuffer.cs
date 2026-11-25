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

    public void Play(bool isLooping, uint realTime)
    {
        if (!GetStatus(realTime))
        {
            PlayedAt = realTime - (uint)Position;
            IsPlaying = true;
        }
        else if (IsLooping && !isLooping && Length > 0)
        {
            PlayedAt = realTime - (uint)((realTime - PlayedAt) % Length);
        }

        IsLooping = isLooping;
    }

    public void Rewind() => Position = 0;

    public uint GetPosition(uint realTime) => IsPlaying ? realTime - PlayedAt : (uint)Position;

    public void Stop(uint realTime)
    {
        if (!GetStatus(realTime))
            return;

        uint played = realTime - PlayedAt;
        Position = IsLooping && Length > 0 ? (int)(played % Length) : (int)played;
        IsPlaying = false;
    }

    public bool GetStatus(uint realTime)
    {
        if (IsPlaying && !IsLooping && realTime - PlayedAt >= Length)
        {
            IsPlaying = false;
            Position = 0;
        }

        return IsPlaying;
    }
}

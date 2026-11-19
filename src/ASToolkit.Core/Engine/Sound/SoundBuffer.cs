namespace ASToolkit.Core.Engine;

internal struct SoundBuffer
{
    public SoundBuffer(float length, int references)
    {
        Length = length;
        References = references;
    }

    public float Length { get; }

    public int References { get; set; }

    public int Position { get; private set; }

    public uint PlayedAt { get; private set; }

    public bool IsPlaying { get; private set; }

    public bool IsLooping { get; private set; }

    public void Play(bool isLooping, uint wallTime)
    {
        if (!IsPlaying)
        {
            PlayedAt = wallTime - (uint)Position;
            IsPlaying = true;
        }

        IsLooping = isLooping;
    }

    public void Rewind() => Position = 0;

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

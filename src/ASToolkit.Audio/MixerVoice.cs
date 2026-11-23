using System.Numerics;

namespace ASToolkit.Audio;

internal struct MixerVoice
{
    public MixerVoice(int sound, int variant, uint startedAt)
    {
        Sound = sound;
        Variant = variant;
        StartedAt = startedAt;
        IsActive = true;
    }

    public int Sound { get; }

    public int Variant { get; }

    public uint StartedAt { get; }

    public bool IsActive { get; }

    public MixedSample Sample { get; set; }

    public long Position { get; set; }

    public bool IsLooping { get; set; }

    public bool IsFinished { get; set; }

    public Vector2 Gains { get; set; }

    public readonly bool IsSame(int sound, int variant, uint startedAt) => IsActive && Sound == sound && Variant == variant && StartedAt == startedAt;
}

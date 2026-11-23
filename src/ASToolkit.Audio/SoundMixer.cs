using System.Numerics;
using ASToolkit.Engine;

namespace ASToolkit.Audio;

internal sealed class SoundMixer
{
    public const int Channels = 2;

    private const int SilentVolume = -10000;
    private const float DecibelScale = 2000;
    private const double MillisecondsPerSecond = 1000;

    private readonly int _rate;
    private readonly ISampleSource _samples;
    private MixerVoice[] _voices = [];
    private bool[] _seen = [];

    public SoundMixer(int rate, ISampleSource samples)
    {
        _rate = rate;
        _samples = samples;
    }

    public static float GetGain(int volume) => volume <= SilentVolume ? 0 : MathF.Pow(10, Math.Min(volume, 0) / DecibelScale);

    public static Vector2 GetPanGains(int pan) => new(pan > 0 ? GetGain(-pan) : 1, pan < 0 ? GetGain(pan) : 1);

    public void BeginSync() => Array.Clear(_seen);

    public void Apply(int buffer, in SoundBufferState state, uint wallTime, bool isQuiet)
    {
        if (buffer >= _voices.Length)
        {
            Array.Resize(ref _voices, buffer + 1);
            Array.Resize(ref _seen, buffer + 1);
        }

        _seen[buffer] = true;
        ref var voice = ref _voices[buffer];
        var gains = GetPanGains(state.Pan) * GetGain(state.Volume);

        if (voice.IsSame(state.Sound, state.Variant, state.StartedAt))
        {
            voice.Gains = gains;
            voice.IsLooping = state.IsLooping;

            return;
        }

        voice = new MixerVoice(state.Sound, state.Variant, state.StartedAt) { Gains = gains, IsLooping = state.IsLooping };

        if (!_samples.TryGetSample(state.Sound, state.Variant, out var sample) || sample.FrameCount == 0)
        {
            voice.IsFinished = true;

            return;
        }

        long offset = (long)((wallTime - state.StartedAt) * (double)_rate / MillisecondsPerSecond);

        if (!state.IsLooping && (isQuiet || offset >= sample.FrameCount))
        {
            voice.IsFinished = true;

            return;
        }

        voice.Sample = sample;
        voice.Position = state.IsLooping ? offset % sample.FrameCount : offset;
    }

    public void EndSync()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_seen[i])
            {
                _voices[i] = default;
            }
        }
    }

    public void Clear() => Array.Clear(_voices);

    public void Mix(Span<float> output)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            ref var voice = ref _voices[i];

            if (voice is { IsActive: true, IsFinished: false })
            {
                MixVoice(ref voice, output);
            }
        }
    }

    private static void MixVoice(ref MixerVoice voice, Span<float> output)
    {
        var samples = voice.Sample.Samples.AsSpan();
        int channels = voice.Sample.Channels;
        int frames = voice.Sample.FrameCount;
        long position = voice.Position;
        for (int i = 0; i + 1 < output.Length; i += Channels)
        {
            if (position >= frames)
            {
                if (!voice.IsLooping)
                {
                    voice.IsFinished = true;

                    break;
                }

                position = 0;
            }

            int at = (int)position * channels;
            float left = samples[at];
            float right = channels > 1 ? samples[at + 1] : left;
            output[i] += left * voice.Gains.X;
            output[i + 1] += right * voice.Gains.Y;
            position++;
        }

        voice.Position = position;
    }
}

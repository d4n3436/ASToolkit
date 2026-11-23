using NVorbis;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Audio;

internal sealed unsafe class MusicTrack : IDisposable
{
    private const int DecodeFrames = 4096;
    private const double MillisecondsPerSecond = 1000;

    private readonly VorbisReader _reader;
    private readonly SDL_AudioStream* _converter;
    private readonly float[] _decoded;
    private float[] _converted = [];
    private bool _isDecoded;

    private MusicTrack(string path, uint startedAt, VorbisReader reader, SDL_AudioStream* converter)
    {
        Path = path;
        StartedAt = startedAt;
        _reader = reader;
        _converter = converter;
        _decoded = new float[DecodeFrames * reader.Channels];
    }

    public string Path { get; }

    public uint StartedAt { get; }

    public float Gain { get; set; }

    public static MusicTrack? Open(string file, string path, uint startedAt, uint wallTime, int rate)
    {
        var reader = new VorbisReader(file);
        long offset = (long)((wallTime - startedAt) * (double)reader.SampleRate / MillisecondsPerSecond);

        if (offset >= reader.TotalSamples)
        {
            reader.Dispose();

            return null;
        }

        reader.SeekTo(offset);
        var source = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = reader.Channels, freq = reader.SampleRate };
        var target = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = SoundMixer.Channels, freq = rate };
        var converter = SDL_CreateAudioStream(&source, &target);

        if (converter == null)
        {
            reader.Dispose();
            return null;
        }

        return new MusicTrack(path, startedAt, reader, converter);
    }

    public void Mix(Span<float> output)
    {
        int bytes = output.Length * sizeof(float);
        while (!_isDecoded && SDL_GetAudioStreamAvailable(_converter) < bytes)
        {
            int count = _reader.ReadSamples(_decoded);

            if (count == 0)
            {
                SDL_FlushAudioStream(_converter);
                _isDecoded = true;

                break;
            }

            fixed (float* decoded = _decoded)
            {
                SDL_PutAudioStreamData(_converter, (nint)decoded, count * sizeof(float));
            }
        }

        if (_converted.Length < output.Length)
        {
            _converted = new float[output.Length];
        }

        int read;
        fixed (float* converted = _converted)
        {
            read = SDL_GetAudioStreamData(_converter, (nint)converted, bytes) / sizeof(float);
        }

        for (int i = 0; i < read; i++)
        {
            output[i] += _converted[i] * Gain;
        }
    }

    public void Dispose()
    {
        SDL_DestroyAudioStream(_converter);
        _reader.Dispose();
    }
}

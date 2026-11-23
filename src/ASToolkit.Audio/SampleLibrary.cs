using System.Text;
using ASToolkit.Formats;
using NVorbis;
using SDL;
using static SDL.SDL3;

namespace ASToolkit.Audio;

internal sealed unsafe class SampleLibrary : ISampleSource
{
    private static ReadOnlySpan<byte> OggExtension => ".ogg"u8;

    private readonly string _gameFolder;
    private readonly IReadOnlyList<SfxRecord> _records;
    private readonly int _rate;
    private readonly MixedSample[] _samples;
    private readonly bool[] _isLoaded;

    public SampleLibrary(string gameFolder, IReadOnlyList<SfxRecord> records, int rate)
    {
        _gameFolder = gameFolder;
        _records = records;
        _rate = rate;
        _samples = new MixedSample[records.Count * SfxRecord.MaxSamples];
        _isLoaded = new bool[_samples.Length];
    }

    public bool TryGetSample(int sound, int variant, out MixedSample sample)
    {
        sample = default;

        if (sound < 0 || sound >= _records.Count || variant < 0 || variant >= SfxRecord.MaxSamples)
            return false;

        int index = sound * SfxRecord.MaxSamples + variant;

        if (!_isLoaded[index])
        {
            _isLoaded[index] = true;
            _samples[index] = Load(_records[sound].GetSamplePath(variant));
        }

        sample = _samples[index];

        return sample.FrameCount > 0;
    }

    private static bool IsOgg(ReadOnlySpan<byte> path) => path.Length >= OggExtension.Length && Ascii.EqualsIgnoreCase(path[^OggExtension.Length..], OggExtension);

    private MixedSample Load(ReadOnlySpan<byte> relative)
    {
        string? path = relative.IsEmpty ? null : GamePath.Resolve(_gameFolder, GameTextEncoding.Cyrillic.GetString(relative));

        if (path is null)
            return default;

        try
        {
            byte[] data = File.ReadAllBytes(path);
            return IsOgg(relative) ? LoadOgg(data) : LoadWave(data);
        }
        catch (Exception)
        {
            return default;
        }
    }

    private MixedSample LoadWave(byte[] data)
    {
        SDL_AudioSpec spec;
        byte* audio;
        uint length;
        fixed (byte* bytes = data)
        {
            var stream = SDL_IOFromConstMem((nint)bytes, (nuint)data.Length);

            if (stream == null || !SDL_LoadWAV_IO(stream, true, &spec, &audio, &length))
                return default;
        }

        try
        {
            return Convert(spec, audio, (int)length);
        }
        finally
        {
            SDL_free(audio);
        }
    }

    private MixedSample LoadOgg(byte[] data)
    {
        using var reader = new VorbisReader(new MemoryStream(data));
        float[] decoded = new float[reader.TotalSamples * reader.Channels];
        int read = 0;
        while (read < decoded.Length)
        {
            int count = reader.ReadSamples(decoded.AsSpan(read));

            if (count == 0)
                break;

            read += count;
        }

        fixed (float* samples = decoded)
        {
            return Convert(new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = reader.Channels, freq = reader.SampleRate }, (byte*)samples, read * sizeof(float));
        }
    }

    private MixedSample Convert(SDL_AudioSpec source, byte* audio, int length)
    {
        int channels = Math.Min(source.channels, SoundMixer.Channels);
        var target = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = channels, freq = _rate };
        byte* converted;
        int convertedLength;

        if (!SDL_ConvertAudioSamples(&source, audio, length, &target, &converted, &convertedLength))
            return default;

        try
        {
            float[] samples = new float[convertedLength / sizeof(float)];
            new ReadOnlySpan<float>(converted, samples.Length).CopyTo(samples);

            return new MixedSample(samples, channels);
        }
        finally
        {
            SDL_free(converted);
        }
    }
}

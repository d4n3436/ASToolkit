using System.Buffers;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal sealed class Sfx
{
    public SoundBuffer[] Buffers { get; } = new SoundBuffer[SfxRecord.MaxSamples];

    public int Count { get; set; }

    [EngineRoutine(0x0041e810)]
    public void Load(Sound sound, int id, SfxRecord record)
    {
        int variants = 0;
        for (int k = 0; k < SfxRecord.MaxSamples; k++)
        {
            var path = record.GetSamplePath(k);

            if (path.IsEmpty)
                break;

            SoundBuffer buffer;
            if (Sound.IsOgg(path))
            {
                int size = 0;
                buffer = sound.LoadOgg(path, id, k, ref size, out var file);

                if (buffer.IsCreated)
                {
                    int decoded = 0;
                    while (true)
                    {
                        int count = sound.Device?.ReadSampleChunk(id, k, file, decoded, Sound.OggReadSize) ?? Math.Min(Sound.OggReadSize, size - decoded);

                        if (count == 0)
                            break;

                        if (count < 0)
                        {
                            sound.Error?.Error("SFX"u8, LogError.Text, count, "decode ogg"u8);
                        }
                        else
                        {
                            decoded += count;
                        }
                    }
                }
            }
            else
            {
                var resource = new Resource();
                buffer = sound.LoadWave(ref resource, path, id, k);

                if (buffer.IsCreated)
                {
                    byte[] samples = ArrayPool<byte>.Shared.Rent(resource.ChunkSize);
                    resource.Read(samples.AsSpan(0, resource.ChunkSize));
                    sound.Device?.FillWaveSampleBuffer(id, k, samples.AsSpan(0, resource.ChunkSize));
                    ArrayPool<byte>.Shared.Return(samples);
                }
            }

            if (!buffer.IsCreated)
                break;

            Buffers[k] = new SoundBuffer(buffer.Length + Sound.MixLatency, 1, id, k);
            variants = k + 1;
        }

        Count = variants;
    }

    [EngineRoutine(0x0041eb30)]
    public int Duplicate(Sound sound, int id, int voice, GameRandom random)
    {
        int variant = random.Next() % Count;
        int handle = id * SfxRecord.MaxSamples + variant;
        ref var original = ref Buffers[variant];
        original.References++;

        if (original.References <= 2)
            return handle;

        original.References--;
        sound.Duplicates[voice] = new SoundBuffer(original.Length, 1, original.Sound, original.Variant);

        return sound.SoundBufferCount + voice;
    }
}

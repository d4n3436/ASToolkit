namespace ASToolkit.Audio;

internal readonly record struct MixedSample(float[] Samples, int Channels)
{
    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
}

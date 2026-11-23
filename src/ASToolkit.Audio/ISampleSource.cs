namespace ASToolkit.Audio;

internal interface ISampleSource
{
    bool TryGetSample(int sound, int variant, out MixedSample sample);
}

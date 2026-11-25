using System.Numerics;
using ASToolkit.Audio;
using ASToolkit.Engine;
using Xunit;

namespace ASToolkit.Tests.Audio;

public class SoundMixerTests
{
    private const int Rate = 1000;

    private static SoundMixer CreateMixer(params float[] samples) => new(Rate, new FixedSamples(new MixedSample(samples, 1)));

    private static float[] Mix(SoundMixer mixer, int frames)
    {
        float[] output = new float[frames * SoundMixer.Channels];
        mixer.Mix(output);

        return output;
    }

    private static void Sync(SoundMixer mixer, SoundBufferState state, uint realTime, bool isQuiet = false)
    {
        mixer.BeginSync();
        mixer.Apply(0, state, realTime, isQuiet);
        mixer.EndSync();
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(-2000, 0.1f)]
    [InlineData(-10000, 0f)]
    [InlineData(500, 1f)]
    public void GetGain_Volume_FollowsHundredthsOfADecibel(int volume, float gain)
    {
        Assert.Equal(gain, SoundMixer.GetGain(volume), 5);
    }

    [Fact]
    public void GetPanGains_PanToTheRight_LowersTheLeftChannel()
    {
        Assert.Equal(new Vector2(0.1f, 1), SoundMixer.GetPanGains(2000));
        Assert.Equal(new Vector2(1, 0.1f), SoundMixer.GetPanGains(-2000));
    }

    [Fact]
    public void Mix_SoundStartedEarlier_PlaysFromItsPosition()
    {
        var mixer = CreateMixer(0.1f, 0.2f, 0.3f, 0.4f);

        Sync(mixer, new SoundBufferState(1, 0, false, 100, 0, 0), 102);

        Assert.Equal([0.3f, 0.3f, 0.4f, 0.4f, 0, 0], Mix(mixer, 3));
    }

    [Fact]
    public void Mix_LoopingSound_WrapsToTheStart()
    {
        var mixer = CreateMixer(0.1f, 0.2f);

        Sync(mixer, new SoundBufferState(1, 0, true, 100, 0, 0), 101);

        Assert.Equal([0.2f, 0.2f, 0.1f, 0.1f, 0.2f, 0.2f], Mix(mixer, 3));
    }

    [Fact]
    public void Mix_PannedSound_AppliesVolumeAndPan()
    {
        var mixer = CreateMixer(1f);

        Sync(mixer, new SoundBufferState(1, 0, false, 100, -2000, 2000), 100);
        float[] output = Mix(mixer, 1);

        Assert.Equal(0.01f, output[0], 5);
        Assert.Equal(0.1f, output[1], 5);
    }

    [Fact]
    public void Apply_QuietSync_ResumesLoopsButNotSoundsThatPlayOnce()
    {
        var once = CreateMixer(0.5f, 0.5f);
        var looping = CreateMixer(0.5f, 0.5f);

        Sync(once, new SoundBufferState(1, 0, false, 100, 0, 0), 100, isQuiet: true);
        Sync(looping, new SoundBufferState(1, 0, true, 100, 0, 0), 100, isQuiet: true);

        Assert.Equal([0f, 0f], Mix(once, 1));
        Assert.Equal([0.5f, 0.5f], Mix(looping, 1));
    }

    [Fact]
    public void Apply_SameBufferAgain_KeepsItsPosition()
    {
        var mixer = CreateMixer(0.1f, 0.2f, 0.3f);
        var state = new SoundBufferState(1, 0, false, 100, 0, 0);

        Sync(mixer, state, 100);
        Mix(mixer, 1);
        Sync(mixer, state, 101);

        Assert.Equal([0.2f, 0.2f], Mix(mixer, 1));
    }

    [Fact]
    public void EndSync_BufferNoLongerPlaying_StopsIt()
    {
        var mixer = CreateMixer(0.5f, 0.5f);

        Sync(mixer, new SoundBufferState(1, 0, false, 100, 0, 0), 100);
        mixer.BeginSync();
        mixer.EndSync();

        Assert.Equal([0f, 0f], Mix(mixer, 1));
    }

    private sealed class FixedSamples : ISampleSource
    {
        private readonly MixedSample _sample;

        public FixedSamples(MixedSample sample)
        {
            _sample = sample;
        }

        public bool TryGetSample(int sound, int variant, out MixedSample sample)
        {
            sample = _sample;

            return true;
        }
    }
}

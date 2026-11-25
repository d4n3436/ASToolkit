using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class SoundTests
{
    private const int MonsterHit = 37;
    private const int Footsteps = 10;
    private const int FrameTime = 17;
    private const int MaxDraws = 1000;

    private static Sound CreateSound() => Sound.Create(new GameFolder(TestGameFolder.RequireFolder()));

    private static int CountDraws(GameRandom random, int startState)
    {
        var reference = new GameRandom(startState);
        int draws = 0;
        while (reference.State != random.State && draws < MaxDraws)
        {
            reference.Next();
            draws++;
        }

        return draws;
    }

    [Fact]
    public void Tact_DeviceEndsAVoice_StopsAskingAboutIt()
    {
        var device = new EndingDevice();
        var sound = Sound.Create(new GameFolder(TestGameFolder.RequireFolder()), device);
        var random = new GameRandom(7);

        sound.PlaySfx(MonsterHit, 0, Sound.FullLoudness);
        sound.Tact(random, 100, 100);
        sound.Tact(random, 100 + FrameTime, 100 + FrameTime);
        sound.Tact(random, 100 + FrameTime * 2, 100 + FrameTime * 2);

        Assert.Equal([0], device.AskedVoices);
    }

    [Fact]
    public void Tact_RequestedSound_DrawsItsVariantOnce()
    {
        var sound = CreateSound();
        var random = new GameRandom(7);

        sound.PlaySfx(MonsterHit, 0, Sound.FullLoudness);
        sound.Tact(random, 100, 100);

        Assert.Equal(1, CountDraws(random, 7));
    }

    [Fact]
    public void PlayAt_FarFromTheScreen_DropsTheRequest()
    {
        var sound = CreateSound();
        var random = new GameRandom(7);

        sound.PlayAt(MonsterHit, new Vector2(1600, 0));
        sound.Tact(random, 100, 100);

        Assert.Equal(7, random.State);
    }

    [Fact]
    public void Tact_SameSoundWithinTheRepeatWindow_StartsOneVoice()
    {
        var sound = CreateSound();
        var random = new GameRandom(7);

        sound.PlaySfx(MonsterHit, 0, Sound.FullLoudness);
        sound.Tact(random, 100, 100);
        sound.PlaySfx(MonsterHit, 0, Sound.FullLoudness);
        sound.Tact(random, 100 + FrameTime, 100 + FrameTime);

        Assert.Equal(1, CountDraws(random, 7));
    }

    [Fact]
    public void Tact_LoopedSoundRequestedEveryFrame_KeepsOneVoice()
    {
        var sound = CreateSound();
        var random = new GameRandom(7);

        for (int frame = 0; frame < 30; frame++)
        {
            sound.PlaySfx(Footsteps, 0, Sound.FullLoudness);
            sound.Tact(random, 100 + frame * FrameTime, (uint)(100 + frame * FrameTime));
        }

        Assert.True(sound.IsLooped(Footsteps));
        Assert.Equal(1, CountDraws(random, 7));
    }

    [Fact]
    public void Tact_MusicTrack_DrawsEachFrameButNotWhileFading()
    {
        var sound = CreateSound();
        var random = new GameRandom(7);
        sound.FadeAndPlayFile(@"music\mus00.ogg"u8, 1, 100);

        sound.Tact(random, 100, 100);
        sound.Tact(random, 100 + FrameTime, 100 + FrameTime);
        int playing = CountDraws(random, 7);
        sound.FadeAndPlayFile(@"music\mus01.ogg"u8, 0, 200);
        int fadeStart = random.State;
        sound.Tact(random, 200, 200);

        Assert.Equal(2, playing);
        Assert.Equal(fadeStart, random.State);
        Assert.True(sound.IsPlayMusic(200));
    }

    [Fact]
    public void TryGetPlayingBuffer_StartedSound_ReportsItsSampleVolumeAndPan()
    {
        var sound = CreateSound();
        var random = new GameRandom(7);

        sound.PlaySfx(MonsterHit, 3000, -500);
        sound.Tact(random, 100, 100);

        var states = GetPlayingBuffers(sound);
        Assert.Equal([new SoundBufferState(MonsterHit, states[0].Variant, false, 100, -500, 3000)], states);
    }

    [Fact]
    public void Stop_SoundOnTwoVoices_LeavesTheOriginalBufferPlaying()
    {
        var records = TestGameFolder.Objects.Sounds;
        int id = Enumerable.Range(0, records.Count).First(i => records[i].VoiceLimit is >= 20 and < 100 && records[i].SampleCount == 1);
        var sound = CreateSound();
        var random = new GameRandom(7);

        sound.PlaySfx(id, 0, Sound.FullLoudness);
        sound.Tact(random, 100, 100);
        sound.PlaySfx(id, 0, Sound.FullLoudness);
        sound.Tact(random, 160, 160);
        int bothPlaying = GetPlayingBuffers(sound).Count(state => state.Sound == id);
        sound.StopSfx(id, 160);

        Assert.Equal(2, bothPlaying);
        Assert.Single(GetPlayingBuffers(sound), state => state.Sound == id && state.StartedAt == 100);
    }

    [Fact]
    public void Tact_SoundStoppedMidPlayThenRequestedAfterItsLength_StartsItAgain()
    {
        var records = TestGameFolder.Objects.Sounds;
        int id = Enumerable.Range(0, records.Count).First(i => records[i].VoiceLimit is > 0 and < 100 && records[i].SampleCount == 1);
        var sound = CreateSound();
        var random = new GameRandom(7);

        sound.PlaySfx(id, 0, Sound.FullLoudness);
        sound.Tact(random, 100, 100);
        sound.StopSfx(id, 100);
        sound.PlaySfx(id, 0, Sound.FullLoudness);
        sound.Tact(random, 60000, 60000);

        Assert.Single(GetPlayingBuffers(sound), state => state.Sound == id && state.StartedAt == 60000);
    }

    [Fact]
    public void TryGetPlayingMusic_StartedTrack_ReportsItsPathAndStart()
    {
        var sound = CreateSound();

        sound.FadeAndPlayFile(@"music\mus00.ogg"u8, 1, 100);

        Assert.True(sound.TryGetPlayingMusic(out var state));
        Assert.Equal(new MusicState(@"music\mus00.ogg", 100, 0), state);
    }

    private static List<SoundBufferState> GetPlayingBuffers(Sound sound)
    {
        var states = new List<SoundBufferState>();
        for (int buffer = 0; buffer < sound.BufferCount; buffer++)
        {
            if (sound.TryGetPlayingBuffer(buffer, out var state))
            {
                states.Add(state);
            }
        }

        return states;
    }

    private sealed class EndingDevice : ISoundDevice
    {
        public List<int> AskedVoices { get; } = [];

        public bool? IsVoicePlaying(int voice)
        {
            AskedVoices.Add(voice);

            return false;
        }

        public bool? IsMusicPlaying() => null;
    }
}

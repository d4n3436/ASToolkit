using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class SoundSystemTests
{
    private const int MonsterHit = 37;
    private const int Footsteps = 10;
    private const int FrameTime = 17;
    private const int MaxDraws = 1000;

    private static SoundSystem CreateSound() => new(TestGameFolder.Objects.Sounds, new GameFolder(TestGameFolder.RequireFolder()));

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
        var sound = new SoundSystem(TestGameFolder.Objects.Sounds, new GameFolder(TestGameFolder.RequireFolder()), device);
        var random = new GameRandom(7);

        sound.Play(MonsterHit, 0, SoundSystem.FullLoudness);
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

        sound.Play(MonsterHit, 0, SoundSystem.FullLoudness);
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

        sound.Play(MonsterHit, 0, SoundSystem.FullLoudness);
        sound.Tact(random, 100, 100);
        sound.Play(MonsterHit, 0, SoundSystem.FullLoudness);
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
            sound.Play(Footsteps, 0, SoundSystem.FullLoudness);
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
        sound.PlayMusicFile(@"music\mus00.ogg"u8, 1, 100);

        sound.Tact(random, 100, 100);
        sound.Tact(random, 100 + FrameTime, 100 + FrameTime);
        int playing = CountDraws(random, 7);
        sound.PlayMusicFile(@"music\mus01.ogg"u8, 0, 200);
        int fadeStart = random.State;
        sound.Tact(random, 200, 200);

        Assert.Equal(2, playing);
        Assert.Equal(fadeStart, random.State);
        Assert.True(sound.IsPlayMusic(200));
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

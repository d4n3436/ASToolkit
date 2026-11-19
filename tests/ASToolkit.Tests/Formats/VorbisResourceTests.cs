using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class VorbisResourceTests
{
    [Fact]
    public void SampleCount_MusicTrack_ReadsTheHeaderAndTheLastGranule()
    {
        var track = new VorbisResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Music", "mus00.ogg")));

        Assert.InRange(track.Channels, 1, 2);
        Assert.Equal(44100, track.SampleRate);
        Assert.InRange(track.SampleCount / track.SampleRate, 10, 600);
    }

    [Fact]
    public void Channels_NotAnOggFile_IsZero()
    {
        var track = new VorbisResource("RIFF0000WAVE"u8.ToArray());

        Assert.Equal(0, track.Channels);
        Assert.Equal(0, track.SampleRate);
        Assert.Equal(0, track.SampleCount);
    }
}

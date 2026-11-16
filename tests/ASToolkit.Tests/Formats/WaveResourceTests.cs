using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class WaveResourceTests
{
    [Fact]
    public void Format_ShortFmtChunk_ReadsThePastTheEndBytesAsTheEngine()
    {
        var wave = new WaveResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Wav", "pistol_shot.wav")));

        Assert.Equal(1, wave.Format.FormatTag);
        Assert.Equal(1, wave.Format.Channels);
        Assert.Equal(0x6164, wave.Format.ExtraSize);
        Assert.Equal(0, wave.Samples.Length % wave.Format.BlockAlign);
        Assert.False(wave.Samples.IsEmpty);
    }
}

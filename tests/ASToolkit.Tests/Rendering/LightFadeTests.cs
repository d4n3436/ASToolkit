using ASToolkit.Core.Rendering;
using Xunit;
using ASToolkit.Core.Formats;

namespace ASToolkit.Tests.Rendering;

public class LightFadeTests
{
    [Theory]
    [InlineData(0x606060u, 0xc0c0c0u, 0u)]
    [InlineData(0x7f0e7fu, 0xfe1cfeu, 0u)]
    [InlineData(0x80feffu, 0x1000000u, 0xfe0200u)]
    [InlineData(0x1d313du, 0x3a627au, 0u)]
    public void FromScript_ExpandsToPaletteStrength(uint gamma, uint negative, uint positive)
    {
        Assert.Equal(new Gamma(negative, positive), Gamma.FromScript(gamma));
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(512, 0x606060u)]
    [InlineData(1020, 0xc0c0c0u)]
    [InlineData(1024, 0xc0c0c0u)]
    public void At_Blackout_UsesTheScriptTimeAndPaletteStrength(int elapsed, uint negative)
    {
        var fade = new LightFade(1000, default, Gamma.FromScript(0x606060));

        Assert.Equal(new Gamma(negative, 0), fade.At(1000 + elapsed));
    }

    [Fact]
    public void At_InitialLighting_KeepsItsFullPalettePrecision()
    {
        var initial = new Gamma(0x113355, 0);
        var fade = new LightFade(0, initial, initial);
        Assert.Equal(initial, fade.At(0));
        Assert.Equal(initial, fade.At(5000));
    }
}

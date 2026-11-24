using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class GammaTests
{
    private const string BuiltInScript = """
        #include "maps\export.lgc"
        int Read=0;
        int Blended=0;
        int Darkest=0;
        main()
        {
          SetGamma(0x606060);
          Read = GetGamma();
          Blended = CountGamma(0, 0x606060, 128);
          Darkest = CountGamma(0, 0x606060, 255);
        }
        """;

    private const string ShiftScript = """
        #include "maps\export.lgc"
        static int Started=0;
        main()
        {
          if( !Started && GetTime() >= 100 )
          {
            Started = 1;
            Effect(EFF_SHIFT_GAMMA, 0x606060, 0, 1000);
          }
        }
        """;

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
    [InlineData(0x606060u)]
    [InlineData(0x7f0e7fu)]
    [InlineData(0x80fe7eu)]
    public void ToScript_ScriptGamma_ReadsBackTheSameGamma(uint gamma)
    {
        Assert.Equal(gamma, Gamma.FromScript(gamma).ToScript());
    }

    [Fact]
    public void Step_GammaBuiltIns_SetReadAndBlendTheScreenGamma()
    {
        var map = EngineTestMap.CreateMap(script: BuiltInScript);

        map.Step();

        Assert.Equal(new Gamma(0xc0c0c0, 0), map.Gamma);
        Assert.Equal(0x606060, map.ReadScriptNumber("Read"u8));
        Assert.Equal(0x303030, map.ReadScriptNumber("Blended"u8));
        Assert.Equal(0x606060, map.ReadScriptNumber("Darkest"u8));
    }

    [Fact]
    public void Constructor_MapAmbient_StartsTheScreenGamma()
    {
        var ambient = new Gamma(0x113355, 0);

        var map = EngineTestMap.CreateMap(ambient: ambient);

        Assert.Equal(ambient, map.Gamma);
    }

    [Fact]
    public void Step_ShiftGammaEffect_MovesTheScreenGammaToItsTargetOverItsDuration()
    {
        var map = EngineTestMap.CreateMap(script: ShiftScript);
        var target = Gamma.FromScript(0x606060);

        while (map.Time < 100)
        {
            map.Play(default);
        }

        int started = map.Time;
        while (map.Time - started < 500)
        {
            map.Play(default);
        }

        var halfway = map.Gamma;
        Assert.InRange(halfway.Negative & 0xff, 0x50u, 0x70u);
        Assert.Equal(0u, halfway.Positive);
        while (map.Time - started < 1100)
        {
            map.Play(default);
        }

        Assert.Equal(target, map.Gamma);
    }
}

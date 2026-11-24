using ASToolkit.Engine.IO;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class FtolTests
{
    [Theory]
    [InlineData(1.9, 1)]
    [InlineData(-1.9, -1)]
    [InlineData(2147483647.0, int.MaxValue)]
    [InlineData(2147483648.0, int.MinValue)]
    [InlineData(4294967301.0, 5)]
    [InlineData(-2147483649.0, int.MaxValue)]
    [InlineData(9223372036854775808.0, 0)]
    [InlineData(-9223372036854775808.0, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.NaN, 0)]
    public void Ftol_Value_KeepsTheLowDwordOfTheTruncatedQword(double value, int expected)
    {
        Assert.Equal(expected, CRuntime.Ftol(value));
    }

    [Fact]
    public void Ftol_LargestIntAsFloat_WrapsToTheSmallestInt()
    {
        Assert.Equal(int.MinValue, CRuntime.Ftol((float)int.MaxValue));
    }
}

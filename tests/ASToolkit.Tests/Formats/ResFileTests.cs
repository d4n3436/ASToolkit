using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class ResFileTests
{
    [Fact]
    public void Build_EveryMapAndObjectTable_ReproducesTheFile()
    {
        string game = TestGameFolder.RequireFolder();
        var files = Directory.EnumerateFiles(Path.Combine(game, "Maps"), "*.map", SearchOption.AllDirectories)
            .Append(Path.Combine(game, "objects.res"))
            .ToList();
        Assert.NotEmpty(files);
        foreach (string path in files)
        {
            byte[] data = File.ReadAllBytes(path);
            Assert.True(data.AsSpan().SequenceEqual(ResFile.Parse(data).Build()), path);
        }
    }
}

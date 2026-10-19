using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class ResContainerBuilderTests
{
    [Fact]
    public void Build_EveryMapMenuAndObjectTable_ReproducesTheFile()
    {
        string game = TestGameFolder.RequireFolder();

        // Skip files the installer patched, the backup is the game's own file
        var files = Directory.EnumerateFiles(game, "*.map", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(game, "*.men*", SearchOption.AllDirectories).Where(f => !File.Exists(f + ".orig")))
            .Append(Path.Combine(game, "objects.res"))
            .ToList();
        Assert.NotEmpty(files);
        foreach (string path in files)
        {
            byte[] data = File.ReadAllBytes(path);
            Assert.True(data.AsSpan().SequenceEqual(ResContainerBuilder.Parse(data).Build()), path);
        }
    }

    [Fact]
    public void Build_ChangedRecords_UpdateTheCountSizesAndPadding()
    {
        string game = TestGameFolder.RequireFolder();
        var builder = ResContainerBuilder.Parse(File.ReadAllBytes(Path.Combine(game, "objects.res")));
        var chunk = builder.GetChunk("OBJ ");
        chunk.Records = chunk.Records.Take(3).Append([1, 2, 3]).ToList();

        byte[] bytes = builder.Build();
        var written = new ResContainer(bytes);
        var back = written.Chunks.First(c => c.Tag.Span.SequenceEqual("OBJ "u8));
        Assert.Equal(4u, back.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, back.Records.Last().ToArray());
        Assert.Equal(bytes.Length - 8, written.Size);
    }
}

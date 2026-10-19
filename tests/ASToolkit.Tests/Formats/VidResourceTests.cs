using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class VidResourceTests
{
    // Run-length sprites; the rest are textures, lights and additive sprites
    private const VidFlags RunLengthRequired = VidFlags.Unknown01 | VidFlags.CurrentVersion;
    private const VidFlags RunLengthOptional = VidFlags.Blended | VidFlags.Depth | VidFlags.Palette;

    [Fact]
    public void GetFrame_GameSprites_KeepEveryPixelInsideTheCanvas()
    {
        string folder = TestGameFolder.RequireFolder();
        int frames = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*.vid", SearchOption.AllDirectories))
        {
            var sprite = new VidResource(File.ReadAllBytes(path));
            if ((sprite.Flags & ~RunLengthOptional) != RunLengthRequired)
            {
                continue;
            }

            Assert.Equal(sprite.HasPalette ? 1024 : 0, sprite.Palette.Length);
            for (int index = 0; index < sprite.FrameCount; index++)
            {
                var frame = sprite.GetFrame(index);
                int lastRow = frame.Top - 1;
                foreach (var run in frame)
                {
                    Assert.True(run is { Count: > 0, X: >= 0 } && run.X + run.Count <= sprite.Width, $"{path} frame {index} x {run.X} count {run.Count}");
                    Assert.True(run.Y >= lastRow && run.Y >= frame.Top && run.Y < frame.Top + frame.RowCount, $"{path} frame {index} row {run.Y}");
                    lastRow = run.Y;
                }

                frames++;
            }
        }

        Assert.True(frames > 0);
    }
}

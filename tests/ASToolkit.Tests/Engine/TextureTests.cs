using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public sealed class TextureTests
{
    [Fact]
    public void Constructor_PaletteTexelsWithAlphaPalette_LookUpThePalette()
    {
        using var session = new GameSession(Playthrough.DisplaySettings, Playthrough.AdapterModes);
        uint[] palette = new uint[256];
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = (uint)(i << 24 | (255 - i) << 16 | i << 8 | i / 2);
        }

        byte[] indexes = new byte[16];
        for (int i = 0; i < indexes.Length; i++)
        {
            indexes[i] = (byte)(i * 17);
        }

        var texture = new Texture(indexes, new Size(4, 4), VidPixelFormat.P8, palette, TextureFlags.HasAlpha | TextureFlags.AlphaPalette, session);

        Assert.Equal(VidPixelFormat.A8R8G8B8, texture.Format);
        var texels = MemoryMarshal.Cast<ushort, uint>(texture.Texels);
        for (int i = 0; i < indexes.Length; i++)
        {
            Assert.Equal(palette[indexes[i]], texels[i]);
        }
    }

    [Fact]
    public void Constructor_PaletteTexelsWithoutPalette_Throws()
    {
        using var session = new GameSession(Playthrough.DisplaySettings, Playthrough.AdapterModes);
        Assert.Throws<NotSupportedException>(() => new Texture(new byte[16], new Size(4, 4), VidPixelFormat.P8, default, TextureFlags.HasAlpha | TextureFlags.AlphaPalette, session));
    }
}

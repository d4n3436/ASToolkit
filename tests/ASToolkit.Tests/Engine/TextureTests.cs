using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public sealed class TextureTests
{
    [Fact]
    public void Create_PaletteTexelsWithAlphaPalette_LookUpThePalette()
    {
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

        var texture = Texture.Create(indexes, new Size(4, 4), VidPixelFormat.P8, palette, TextureFlags.HasAlpha | TextureFlags.AlphaPalette, null);

        Assert.Equal(VidPixelFormat.A8R8G8B8, texture.Format);
        var texels = MemoryMarshal.Cast<ushort, uint>(texture.Texels.AsSpan());
        for (int i = 0; i < indexes.Length; i++)
        {
            Assert.Equal(palette[indexes[i]], texels[i]);
        }
    }

    [Fact]
    public void Create_PaletteTexelsWithoutPalette_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Texture.Create(new byte[16], new Size(4, 4), VidPixelFormat.P8, default, TextureFlags.HasAlpha | TextureFlags.AlphaPalette, null));
    }
}

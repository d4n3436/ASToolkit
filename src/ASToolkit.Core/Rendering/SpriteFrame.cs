using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

// The pixels of one sprite frame, row by row from the top, each with a column and a palette index, and a depth for sprites
// with a depth buffer. The palette is the object's tinted one, shared by all frames of the sprite and tint.
internal sealed class SpriteFrame
{
    public required short[] Xs { get; init; }

    public required byte[] Indices { get; init; }

    public required ushort[] Depths { get; init; }

    public required Rgb24[] Colors { get; init; }

    public required byte[] Alphas { get; init; }

    public required int Top { get; init; }

    public required int Rows { get; init; }

    // The index of the first pixel of each row from the top, and the pixel count after the last row.
    public required int[] RowStarts { get; init; }

    public required bool IsBlended { get; init; }
}

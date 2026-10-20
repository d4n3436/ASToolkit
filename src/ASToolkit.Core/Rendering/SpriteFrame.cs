using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

// Pixels of one frame, row by row from the top, with a column, palette index and optional depth each.
// The palette is the object's tinted and brightness-scaled one, shared by all frames with the same tint and brightness.
internal sealed class SpriteFrame
{
    public required short[] Xs { get; init; }

    public required byte[] Indices { get; init; }

    public required ushort[] Depths { get; init; }

    public required Rgb24[] Colors { get; init; }

    public required byte[] Alphas { get; init; }

    public required int Top { get; init; }

    public required int Rows { get; init; }

    // Index of each row's first pixel, plus the total pixel count at the end
    public required int[] RowStarts { get; init; }

    public required bool IsBlended { get; init; }
}

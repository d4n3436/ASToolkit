using ASToolkit.Core.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace ASToolkit.Core.Rendering;

// Pixels of one frame on its canvas, row by row from the top, with a column, palette index and optional depth each.
// The palette is the object's tinted one, shared by all frames with the same tint.
internal sealed class SpriteFrame
{
    public required short[] Xs { get; init; }

    public required byte[] Indices { get; init; }

    public required ushort[] Depths { get; init; }

    public required Rgba32[] Colors { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Top { get; init; }

    public required int Rows { get; init; }

    // Index of each row's first pixel, plus the total pixel count at the end
    public required int[] RowStarts { get; init; }

    public required bool IsBlended { get; init; }

    public required VidBlend Blend { get; init; }

    public bool IsCombined => Blend is VidBlend.Multiply or VidBlend.Additive or VidBlend.AdditiveLight or VidBlend.Brighten;

    public required bool WritesDepth { get; init; }

    // True when Indices holds palette indices into Colors; false when Colors holds one entry per pixel
    public required bool IsPaletted { get; init; }
}

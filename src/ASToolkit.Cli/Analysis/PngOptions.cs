using SixLabors.ImageSharp.Formats.Png;

namespace ASToolkit.Cli.Analysis;

internal static class PngOptions
{
    public static PngEncoder Fast { get; } = new() { CompressionLevel = PngCompressionLevel.BestSpeed };
}

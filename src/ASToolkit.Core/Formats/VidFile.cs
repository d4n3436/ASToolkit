using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a .vid sprite file with run-length frames, optionally with a palette and a per-pixel depth buffer.
/// </summary>
public sealed class VidFile
{
    private const int MaxAliasHops = 8;
    private const int FrameHeader = 4;

    private readonly List<byte[]> _records;
    private readonly ConcurrentDictionary<int, VidFrame> _frames = new();

    private VidFile(int type, int width, int height, List<byte[]> records, byte[] palette)
    {
        Type = type;
        Width = width;
        Height = height;
        _records = records;
        Palette = palette;
    }

    /// <summary>
    /// Gets the sprite type flags.
    /// </summary>
    public int Type { get; }

    /// <summary>
    /// Gets the canvas width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the canvas height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public int FrameCount => _records.Count;

    /// <summary>
    /// Gets a value indicating whether the frames carry a per-pixel depth buffer.
    /// </summary>
    public bool HasDepth => (Type & 4) != 0;

    /// <summary>
    /// Gets a value indicating whether the pixel colors are palette indices.
    /// </summary>
    public bool HasPalette => (Type & 8) != 0;

    /// <summary>
    /// Gets a value indicating whether the sprite blends with what lies under it instead of covering it.
    /// </summary>
    public bool IsBlended => (Type & 2) != 0;

    /// <summary>
    /// Gets the 256 palette entries as blue, green, red and alpha bytes, or an empty array when the file has no palette.
    /// </summary>
    public byte[] Palette { get; }

    /// <summary>
    /// Reads a sprite file.
    /// </summary>
    public static VidFile Load(string path)
    {
        var file = ResFile.Load(path);
        ReadOnlySpan<byte> head = file.GetChunk("HEAD").Records[0];
        return new VidFile(BinaryPrimitives.ReadUInt16LittleEndian(head), BinaryPrimitives.ReadUInt16LittleEndian(head[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(head[8..]), file.FindChunk("DATA")?.Records ?? [], file.FindChunk("PAL ")?.Records[0] ?? []);
    }

    /// <summary>
    /// Returns the pixels of a frame, following frames that repeat another one.
    /// </summary>
    public VidFrame GetFrame(int index) => _frames.GetOrAdd(index, i => Decode(i, []));

    /// <summary>
    /// Returns the pixels of a frame with the color of each: palette indices for paletted sprites, 16-bit colors
    /// otherwise. Unlike <see cref="GetFrame"/>, it decodes the frame on every call and keeps nothing.
    /// </summary>
    public (VidFrame Frame, ushort[] Colors) DecodeColors(int index)
    {
        var colors = new List<ushort>();
        var frame = Decode(index, colors);
        return (frame, colors.ToArray());
    }

    private VidFrame Decode(int index, List<ushort> colors)
    {
        int target = index;
        for (int hop = 0; hop < MaxAliasHops && IsAlias(target); hop++)
        {
            target = BinaryPrimitives.ReadInt16LittleEndian(_records[target].AsSpan(FrameHeader));
        }

        return IsAlias(target) ? new VidFrame([], [], []) : Decode(_records[target].AsSpan(FrameHeader), colors);
    }

    // Alias frames hold only the index of the frame they repeat
    private bool IsAlias(int index) => _records[index].Length == FrameHeader + 2;

    private VidFrame Decode(ReadOnlySpan<byte> b, List<ushort> colors)
    {
        bool depth = HasDepth;
        bool palette = HasPalette;
        var xs = new List<int>();
        var ys = new List<int>();
        var zs = new List<int>();
        int p = 2 + 6 * BinaryPrimitives.ReadInt16LittleEndian(b);
        if (p + 4 > b.Length)
        {
            return new VidFrame([], [], []);
        }

        int y0 = BinaryPrimitives.ReadInt16LittleEndian(b[p..]);
        int rowCount = BinaryPrimitives.ReadInt16LittleEndian(b[(p + 2)..]);
        p += 4;
        for (int row = 0; row < rowCount; row++)
        {
            int x = 0;
            while (true)
            {
                int skip = b[p];
                int count = b[p + 1];
                p += 2;
                if (skip == 0 && count == 0)
                {
                    break;
                }

                x += skip;
                int colorStart = p + (depth ? 2 * count : 0);
                for (int index = 0; index < count; index++)
                {
                    xs.Add(x + index);
                    ys.Add(y0 + row);
                    zs.Add(depth ? BinaryPrimitives.ReadUInt16LittleEndian(b[(p + 2 * index)..]) : 0);
                    colors.Add(palette ? b[colorStart + index] : BinaryPrimitives.ReadUInt16LittleEndian(b[(colorStart + 2 * index)..]));
                }

                int colorSize = palette ? 1 : 2;
                p += (depth ? 2 * count : 0) + colorSize * count;
                x += count;
            }
        }

        return new VidFrame(xs.ToArray(), ys.ToArray(), zs.ToArray());
    }
}

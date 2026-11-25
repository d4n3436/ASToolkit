using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a read-only view of a Windows cursor the game loads from its cursor folder, either a plain cursor or the first frame of an animated one.
/// </summary>
public readonly struct CursorImage
{
    private const int RiffHeader = 12;
    private const int ChunkHeader = 8;
    private const int IconDirectory = 6;
    private const int CursorType = 2;
    private const int RowAlignment = 4;
    private const int BitsPerByte = 8;
    private const int HighBit = 0x80;
    private const int AlphaBytes = 4;
    private const int PaletteEntryBytes = 4;
    private const int MaxPaletteBits = 8;

    private readonly ReadOnlyMemory<byte> _palette;
    private readonly ReadOnlyMemory<byte> _colors;
    private readonly ReadOnlyMemory<byte> _mask;
    private readonly int _colorStride;
    private readonly int _maskStride;
    private readonly CursorEntry _entry;
    private readonly CursorBitmapHeader _bitmap;

    /// <summary>
    /// Initializes a new instance of the <see cref="CursorImage"/> structure.
    /// </summary>
    /// <param name="data">The data of the cursor file.</param>
    public CursorImage(ReadOnlyMemory<byte> data)
    {
        int icon = data.Span.StartsWith("RIFF"u8) ? FindIcon(data.Span) : IsPlainCursor(data.Span) ? 0 : -1;

        if (icon < 0 || icon + IconDirectory + Unsafe.SizeOf<CursorEntry>() > data.Length)
            return;

        ref readonly var entry = ref MemoryMarshal.AsRef<CursorEntry>(data.Span[(icon + IconDirectory)..]);
        long offset = icon + entry.Offset;

        if (offset + Unsafe.SizeOf<CursorBitmapHeader>() > data.Length)
            return;

        ref readonly var bitmap = ref MemoryMarshal.AsRef<CursorBitmapHeader>(data.Span[(int)offset..]);
        int height = bitmap.Height / 2;

        if (bitmap.BitCount is not (1 or 4 or 8 or 24 or 32) || bitmap.Width <= 0 || height <= 0 || bitmap.Size < Unsafe.SizeOf<CursorBitmapHeader>() ||
            offset + bitmap.Size > data.Length)
            return;

        int paletteEntries = bitmap.BitCount > MaxPaletteBits ? 0 : bitmap.ColorsUsed != 0 ? (int)Math.Min(bitmap.ColorsUsed, 1u << bitmap.BitCount) : 1 << bitmap.BitCount;
        int colorStride = GetStride((bitmap.Width * bitmap.BitCount + BitsPerByte - 1) / BitsPerByte);
        int maskStride = GetStride((bitmap.Width + BitsPerByte - 1) / BitsPerByte);
        var palette = data[(int)(offset + bitmap.Size)..];

        if ((long)paletteEntries * PaletteEntryBytes + (long)(colorStride + maskStride) * height > palette.Length)
            return;

        var pixels = palette[(paletteEntries * PaletteEntryBytes)..];
        _entry = entry;
        _bitmap = bitmap;
        _colorStride = colorStride;
        _maskStride = maskStride;
        _palette = palette[..(paletteEntries * PaletteEntryBytes)];
        _colors = pixels[..(colorStride * height)];
        _mask = pixels.Slice(colorStride * height, maskStride * height);
    }

    /// <summary>
    /// Gets a value indicating whether the cursor holds an image to draw.
    /// </summary>
    public bool IsValid => !_colors.IsEmpty;

    /// <summary>
    /// Gets the size of the cursor in pixels.
    /// </summary>
    public Size Size => new(_bitmap.Width, _bitmap.Height / 2);

    /// <summary>
    /// Gets the pixel of the cursor's hot spot, counted from its top left corner.
    /// </summary>
    public Point Hotspot => new(_entry.HotspotX, _entry.HotspotY);

    /// <summary>
    /// Attempts to get the color of a pixel of the cursor.
    /// </summary>
    /// <param name="pixel">The pixel, counted from the top left corner.</param>
    /// <param name="color">The color of the pixel if it is drawn; otherwise, the default color.</param>
    /// <returns><see langword="true"/> if the pixel is drawn; otherwise, <see langword="false"/>.</returns>
    public bool TryGetPixel(Point pixel, out Color color)
    {
        color = default;
        var size = Size;

        if (pixel.X < 0 || pixel.X >= size.Width || pixel.Y < 0 || pixel.Y >= size.Height)
            return false;

        int source = size.Height - 1 - pixel.Y;

        if ((_mask.Span[source * _maskStride + pixel.X / BitsPerByte] & (HighBit >> (pixel.X % BitsPerByte))) != 0)
            return false;

        var row = _colors.Span[(source * _colorStride)..];
        int bitCount = _bitmap.BitCount;

        if (bitCount <= MaxPaletteBits)
        {
            int bit = pixel.X * bitCount;
            int index = row[bit / BitsPerByte] >> (BitsPerByte - bitCount - bit % BitsPerByte) & ((1 << bitCount) - 1);

            if ((index + 1) * PaletteEntryBytes > _palette.Length)
                return false;

            var entry = _palette.Span[(index * PaletteEntryBytes)..];
            color = Color.FromArgb(entry[2], entry[1], entry[0]);

            return true;
        }

        int bytes = bitCount / BitsPerByte;
        var bgr = row[(pixel.X * bytes)..];

        if (bytes == AlphaBytes && bgr[3] == 0)
            return false;

        color = Color.FromArgb(bgr[2], bgr[1], bgr[0]);

        return true;
    }

    private static int GetStride(int bytes) => (bytes + RowAlignment - 1) / RowAlignment * RowAlignment;

    private static bool IsPlainCursor(ReadOnlySpan<byte> data) =>
        data.Length >= IconDirectory && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == CursorType;

    private static int FindIcon(ReadOnlySpan<byte> data)
    {
        int position = RiffHeader;
        while (position + ChunkHeader <= data.Length)
        {
            var id = data.Slice(position, 4);
            int size = MemoryMarshal.Read<int>(data[(position + 4)..]);

            if (size < 0)
                return -1;

            if (id.SequenceEqual("icon"u8))
                return position + ChunkHeader;

            if (id.SequenceEqual("LIST"u8))
            {
                position += ChunkHeader + 4;
                continue;
            }

            position += ChunkHeader + size + (size & 1);
        }

        return -1;
    }
}

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a read-only view of the first frame of a Windows animated cursor, the kind the game loads from its cursor folder.
/// </summary>
public readonly struct CursorImage
{
    private const int RiffHeader = 12;
    private const int ChunkHeader = 8;
    private const int IconDirectory = 6;
    private const int RowAlignment = 4;
    private const int BitsPerByte = 8;
    private const int HighBit = 0x80;
    private const int AlphaBytes = 4;

    private readonly ReadOnlyMemory<byte> _colors;
    private readonly ReadOnlyMemory<byte> _mask;
    private readonly int _colorStride;
    private readonly int _maskStride;
    private readonly CursorEntry _entry;
    private readonly BitmapHeader _bitmap;

    /// <summary>
    /// Initializes a new instance of the <see cref="CursorImage"/> structure.
    /// </summary>
    /// <param name="data">The data of the animated cursor file.</param>
    public CursorImage(ReadOnlyMemory<byte> data)
    {
        int icon = FindIcon(data.Span);

        if (icon < 0 || icon + IconDirectory + Unsafe.SizeOf<CursorEntry>() > data.Length)
            return;

        ref readonly var entry = ref MemoryMarshal.AsRef<CursorEntry>(data.Span[(icon + IconDirectory)..]);
        long offset = icon + entry.Offset;

        if (offset + Unsafe.SizeOf<BitmapHeader>() > data.Length)
            return;

        ref readonly var bitmap = ref MemoryMarshal.AsRef<BitmapHeader>(data.Span[(int)offset..]);
        int height = bitmap.Height / 2;

        if (bitmap.BitCount is not (24 or 32) || bitmap.Width <= 0 || height <= 0 || bitmap.Size < Unsafe.SizeOf<BitmapHeader>() ||
            offset + bitmap.Size > data.Length)
            return;

        int colorStride = GetStride(bitmap.Width * bitmap.BitCount / BitsPerByte);
        int maskStride = GetStride((bitmap.Width + BitsPerByte - 1) / BitsPerByte);
        var pixels = data[(int)(offset + bitmap.Size)..];

        if ((long)(colorStride + maskStride) * height > pixels.Length)
            return;

        _entry = entry;
        _bitmap = bitmap;
        _colorStride = colorStride;
        _maskStride = maskStride;
        _colors = pixels[..(colorStride * height)];
        _mask = pixels.Slice(colorStride * height, maskStride * height);
    }

    /// <summary>
    /// Gets a value indicating whether the cursor holds a 24- or 32-bit icon to draw.
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

        int bytes = _bitmap.BitCount / BitsPerByte;
        int source = size.Height - 1 - pixel.Y;

        if ((_mask.Span[source * _maskStride + pixel.X / BitsPerByte] & (HighBit >> (pixel.X % BitsPerByte))) != 0)
            return false;

        var bgr = _colors.Span[(source * _colorStride + pixel.X * bytes)..];

        if (bytes == AlphaBytes && bgr[3] == 0)
            return false;

        color = Color.FromArgb(bgr[2], bgr[1], bgr[0]);

        return true;
    }

    private static int GetStride(int bytes) => (bytes + RowAlignment - 1) / RowAlignment * RowAlignment;

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

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private readonly struct CursorEntry
    {
        [FieldOffset(4)]
        public readonly ushort HotspotX;

        [FieldOffset(6)]
        public readonly ushort HotspotY;

        [FieldOffset(12)]
        public readonly uint Offset;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private readonly struct BitmapHeader
    {
        [FieldOffset(0)]
        public readonly uint Size;

        [FieldOffset(4)]
        public readonly int Width;

        [FieldOffset(8)]
        public readonly int Height;

        [FieldOffset(14)]
        public readonly ushort BitCount;
    }
}

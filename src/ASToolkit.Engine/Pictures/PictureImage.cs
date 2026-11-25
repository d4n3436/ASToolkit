using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the pixels of the current frame of a picture file.
/// </summary>
public class PictureImage : IDisposable
{
    /// <summary>
    /// Represents the number of bits in a byte.
    /// </summary>
    protected const int BitsPerByte = 8;

    /// <summary>
    /// Represents the playback speed of a picture if its file doesn't specify one.
    /// </summary>
    protected const int DefaultSpeed = 0x47;
    private const int FirstDigitScanOffset = 2;
    private const int MaxNumberLength = 11;
    private const int StackNameLength = 256;
    private const int PaletteSize = 256;

    private readonly uint[] _palette = new uint[PaletteSize];
    private byte[] _pixels = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PictureImage"/> class without pixels.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public PictureImage(string gameFolder)
        : this(gameFolder, null)
    {
    }

    [EngineRoutine(0x00426dd0)]
    internal PictureImage(string gameFolder, MyError? error)
    {
        GameFolder = gameFolder;
        Error = error;
        Name = string.Empty;
        Speed = DefaultSpeed;
    }

    /// <summary>
    /// Gets the game folder the picture files are in.
    /// </summary>
    public string GameFolder { get; }

    /// <summary>
    /// Gets the path of the first picture file, in lower case.
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>
    /// Gets or sets the number of frames.
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the current frame.
    /// </summary>
    public int FrameIndex { get; set; }

    /// <summary>
    /// Gets the playback speed of the picture, used when it's converted to a sprite file.
    /// </summary>
    public int Speed { get; protected set; }

    /// <summary>
    /// Gets the size of the picture, in pixels.
    /// </summary>
    public Size Size { get; private set; }

    /// <summary>
    /// Gets the number of bytes of a pixel, from 1 for palette indexes to 4 for colors with an alpha channel.
    /// </summary>
    public int BytesPerPixel { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the picture has pixels.
    /// </summary>
    public bool HasPixels => _pixels.Length != 0;

    /// <summary>
    /// Gets the pixels row by row from the top, or an empty span if the picture doesn't have any.
    /// </summary>
    public Span<byte> Pixels => _pixels;

    /// <summary>
    /// Gets the palette of a picture with palette indexes, packed as red, green and blue.
    /// </summary>
    public Span<uint> Palette => _palette;

    internal MyError? Error { get; }

    /// <summary>
    /// Gets or sets the open picture file.
    /// </summary>
    protected FileStream? Input { get; set; }

    /// <summary>
    /// Gets or sets the offset of the pixels in the open picture file.
    /// </summary>
    protected int DataOffset { get; set; }

    /// <summary>
    /// Releases the pixels and closes the picture file.
    /// </summary>
    [EngineRoutine(0x00426e10)]
    public void Dispose()
    {
        Reset();
        Name = string.Empty;
    }

    /// <summary>
    /// Sets the dimensions of the picture and clears its pixels.
    /// </summary>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="bytesPerPixel">The number of bytes of a pixel.</param>
    [EngineRoutine(0x00426eb0)]
    public void Resize(Size size, int bytesPerPixel)
    {
        Size = size;
        BytesPerPixel = bytesPerPixel;
        try
        {
            _pixels = new byte[size.Height * bytesPerPixel * size.Width];
        }
        catch (OutOfMemoryException)
        {
            _pixels = [];
            Report(LogError.Memory, "picture buffer"u8, size.Height * bytesPerPixel * size.Width);
        }
    }

    /// <summary>
    /// Releases the pixels, closes the picture file and resets the dimensions.
    /// </summary>
    [EngineRoutine(0x00426f50)]
    public void Reset()
    {
        _pixels = [];
        CloseFile();
        FrameCount = 0;
        Size = Size.Empty;
    }

    /// <summary>
    /// Opens the picture file.
    /// </summary>
    /// <param name="name">The path of the picture file.</param>
    /// <returns><see langword="true"/> if the picture file was opened; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00427610)]
    public virtual bool Load(string name)
    {
        Reset();
        if (name.Length == 0)
        {
            Report(LogError.Invalid, "filename"u8);
            return false;
        }

        byte[] lower = Encoding.ASCII.GetBytes(name);
        CRuntime.ToLowerInPlace(lower);
        Name = Encoding.ASCII.GetString(lower);
        Input = OpenFile(name);
        if (Input is null)
        {
            Report(LogError.Open);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Goes to the next frame, and back to the first frame after the last one.
    /// </summary>
    [EngineRoutine(0x004275f0)]
    public virtual void Advance()
    {
        if (FrameCount == 0)
            return;

        FrameIndex++;
        if (FrameIndex >= FrameCount)
        {
            Rewind();
        }
    }

    /// <summary>
    /// Goes back to the first frame.
    /// </summary>
    [EngineRoutine(0x004275b0)]
    public virtual void Rewind()
    {
        if (!HasPixels)
            return;

        Input?.Seek(DataOffset, SeekOrigin.Begin);
        FrameIndex = -1;
        Advance();
    }

    /// <summary>
    /// Determines whether a point is inside the picture.
    /// </summary>
    /// <param name="point">The point, in pixels from the top left corner.</param>
    /// <returns><see langword="true"/> if the point is inside the picture; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Point point) => new Rectangle(Point.Empty, Size).Contains(point);

    /// <summary>
    /// Gets the raw value of a pixel.
    /// </summary>
    /// <param name="point">The point of the pixel.</param>
    /// <returns>The palette index, 16-bit color, 24-bit color or 32-bit color, or zero outside of the picture.</returns>
    [EngineRoutine(0x004271b0)]
    public uint GetPixel(Point point)
    {
        if (!HasPixels || !Contains(point))
            return 0;

        int offset = (point.Y * Size.Width + point.X) * BytesPerPixel;
        return BytesPerPixel switch
        {
            1 => Pixels[offset],
            2 => MemoryMarshal.Read<ushort>(Pixels[offset..]),
            3 => MemoryMarshal.AsRef<Pixel24>(Pixels[offset..]).Value,
            4 => MemoryMarshal.Read<uint>(Pixels[offset..]),
            _ => 0
        };
    }

    /// <summary>
    /// Sets the raw value of a pixel.
    /// </summary>
    /// <param name="point">The point of the pixel.</param>
    /// <param name="value">The palette index, 16-bit color, 24-bit color or 32-bit color.</param>
    [EngineRoutine(0x00427250)]
    public void SetPixel(Point point, uint value)
    {
        if (!HasPixels || !Contains(point))
            return;

        int offset = (point.Y * Size.Width + point.X) * BytesPerPixel;
        switch (BytesPerPixel)
        {
            case 1:
                Pixels[offset] = (byte)value;
                break;
            case 2:
                MemoryMarshal.Write(Pixels[offset..], (ushort)value);
                break;
            case 3:
                Pixels[offset] = (byte)value;
                Pixels[offset + 1] = (byte)(value >> 8);
                Pixels[offset + 2] = (byte)(value >> 16);
                break;
            case 4:
                MemoryMarshal.Write(Pixels[offset..], value);
                break;
        }
    }

    /// <summary>
    /// Gets the color of a pixel.
    /// </summary>
    /// <param name="point">The point of the pixel.</param>
    /// <returns>The color packed as alpha, red, green and blue, or opaque black for a pixel outside of the picture.</returns>
    [EngineRoutine(0x00427440)]
    public uint GetColor(Point point)
    {
        if (!HasPixels || !Contains(point))
            return 0xff000000;

        uint pixel = GetPixel(point);
        return BytesPerPixel switch
        {
            1 => _palette[pixel],
            2 => ((((pixel & 0xffffff80 | 0xffff8000) << 3 | pixel & 0x3fc) << 3) | pixel & 0x1f) << 3,
            3 or 4 => pixel,
            _ => 0xff000000
        };
    }

    internal void Report(LogError code, ReadOnlySpan<byte> text = default, int value = 0) => Error?.Error($"PICTURE '{Name}'", code, value, text);

    /// <summary>
    /// Returns the path of the file of a frame, made from the path of the first file with its number replaced.
    /// </summary>
    /// <param name="name">The path of the first file.</param>
    /// <param name="index">The index of the frame.</param>
    /// <returns>The path of the file, or an empty string if the path doesn't have a number.</returns>
    [EngineRoutine(0x004070a0)]
    protected static string GetFrameName(string name, int index)
    {
        if (index == 0)
            return name;

        int last = name.Length - FirstDigitScanOffset;
        while (last >= 0 && !IsDigit(name[last]))
        {
            last--;
        }

        if (last < 0)
            return string.Empty;

        int first = last;
        while (first - 1 >= 0 && IsDigit(name[first - 1]))
        {
            first--;
        }

        if (first - 1 >= 0 && name[first - 1] == '-')
        {
            first--;
        }

        int width = last - first + 1;
        int value = 0;
        bool isNegative = name[first] == '-';
        for (int position = isNegative ? first + 1 : first; position < name.Length && char.IsAsciiDigit(name[position]); position++)
        {
            value = unchecked(value * 10 + (name[position] - '0'));
        }

        value = unchecked((isNegative ? -value : value) + index);
        Span<char> number = stackalloc char[Math.Max(width, MaxNumberLength)];
        value.TryFormat(number, out int numberLength, default, CultureInfo.InvariantCulture);
        var digits = number[..numberLength];
        bool isMinus = digits[0] == '-';
        if (isMinus)
        {
            digits = digits[1..];
        }

        var buffer = name.Length <= StackNameLength ? stackalloc char[StackNameLength] : new char[name.Length];
        buffer = buffer[..name.Length];
        name.CopyTo(buffer);
        int zeros = Math.Max(width - digits.Length - (isMinus ? 1 : 0), 0);
        for (int position = 0; position < width; position++)
        {
            char character;
            if (isMinus && position == 0)
            {
                character = '-';
            }
            else
            {
                int digit = position - (isMinus ? 1 : 0) - zeros;
                character = digit < 0 ? '0' : digit < digits.Length ? digits[digit] : '\0';
            }

            buffer[first + position] = character;
        }

        int end = buffer.IndexOf('\0');
        return new string(end < 0 ? buffer : buffer[..end]);
    }

    /// <summary>
    /// Opens a picture file for reading.
    /// </summary>
    /// <param name="name">The path of the picture file, relative to the game folder.</param>
    /// <returns>The open file, or <see langword="null"/> if the file doesn't exist.</returns>
    protected FileStream? OpenFile(string name)
    {
        string? path = GamePath.Resolve(GameFolder, name);
        return path is null ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
    }

    /// <summary>
    /// Closes the open picture file.
    /// </summary>
    protected void CloseFile()
    {
        Input?.Dispose();
        Input = null;
    }

    private protected static bool IsDigit(char character) => character <= byte.MaxValue && CRuntime.IsDigit((byte)character);
}

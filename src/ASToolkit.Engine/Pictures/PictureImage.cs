using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the pixels of one picture, which a picture file reader refills with the next frame of the file when it advances.
/// </summary>
public class PictureImage : IDisposable
{
    private const int FirstDigitScanOffset = 2;
    private const int MaxNumberLength = 11;
    private const int StackNameLength = 256;

    /// <summary>
    /// Represents the number of bits in a byte, the unit that a picture file counts the depth of a pixel in.
    /// </summary>
    protected const int BitsPerByte = 8;

    /// <summary>
    /// Represents the playback speed of a picture whose file doesn't hold one.
    /// </summary>
    protected const int DefaultSpeed = 0x47;

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
    internal PictureImage(string gameFolder, GameLogger? logger)
    {
        GameFolder = gameFolder;
        Logger = logger;
        Name = string.Empty;
        Speed = DefaultSpeed;
    }

    internal GameLogger? Logger { get; }

    /// <summary>
    /// Gets the game folder the picture files are in.
    /// </summary>
    public string GameFolder { get; }

    /// <summary>
    /// Gets the path of the first picture file.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets or sets the number of frames.
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the current frame.
    /// </summary>
    public int FrameIndex { get; set; }

    /// <summary>
    /// Gets the playback speed the picture gets when it becomes a sprite file.
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
    /// Gets the pixels row by row from the top, or an empty span if the picture has none.
    /// </summary>
    public Span<byte> Pixels => _pixels;

    /// <summary>
    /// Gets the palette of a picture with palette indexes, packed as red, green and blue.
    /// </summary>
    public Span<uint> Palette => _palette;

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

    internal void Report(LogError code, ReadOnlySpan<byte> text, int value = 0) => Logger?.Error("PICTURE '%s'"u8, code, value, text, (ReadOnlyMemory<byte>)GameTextEncoding.Cyrillic.GetBytes(Name));

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
        _pixels = new byte[size.Height * bytesPerPixel * size.Width];
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

        Name = name.ToLowerInvariant();
        Input = OpenFile(Name);
        if (Input is null)
        {
            Report(LogError.Open, default);
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
    /// Goes back to before the first frame and then to the first frame.
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
        for (int i = 0; i < BytesPerPixel; i++)
        {
            Pixels[offset + i] = (byte)(value >> i * 8);
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
            _ => pixel
        };
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

    /// <summary>
    /// Determines whether the size and depth of a picture file header fit the pixels the picture already has, which is always the case for a picture without pixels.
    /// </summary>
    /// <param name="size">The size of the picture file, in pixels.</param>
    /// <param name="depth">The number of bits of a pixel in the picture file.</param>
    /// <returns><see langword="true"/> if the picture has no pixels or its dimensions are the ones of the file; otherwise, <see langword="false"/>.</returns>
    protected bool FitsPixels(Size size, int depth) => !HasPixels || Size == size && BytesPerPixel == depth / BitsPerByte;

    /// <summary>
    /// Reads the pixel rows of the open picture file into the pixels, from the bottom row up.
    /// </summary>
    /// <param name="padding">The number of bytes after each row in the file.</param>
    protected void ReadRows(int padding)
    {
        int rowSize = Size.Width * BytesPerPixel;
        for (int y = Size.Height - 1; y >= 0; y--)
        {
            Input!.ReadAtLeast(Pixels.Slice(y * rowSize, rowSize), rowSize, false);
            if (padding != 0)
            {
                Input.Seek(padding, SeekOrigin.Current);
            }
        }
    }

    /// <summary>
    /// Counts the frames of a picture whose files are numbered, as the files after the first one that exist.
    /// </summary>
    /// <param name="name">The path of the first file.</param>
    /// <param name="extension">The extension of the picture files, with the dot.</param>
    protected void CountFrames(string name, string extension)
    {
        FrameCount = 1;
        string stem = name.EndsWith(extension, StringComparison.Ordinal) ? name[..^extension.Length] : name;
        if (stem.Length == 0 || !char.IsAsciiDigit(stem[^1]))
            return;

        while (GamePath.Resolve(GameFolder, GetFrameName(name, FrameCount)) is not null)
        {
            FrameCount++;
        }
    }

    /// <summary>
    /// Gets the path of the file of a frame, which is the path of the first file with its number replaced.
    /// </summary>
    /// <param name="name">The path of the first file.</param>
    /// <param name="index">The index of the frame.</param>
    /// <returns>The path of the file, or an empty string if the path has no number.</returns>
    [EngineRoutine(0x004070a0)]
    protected static string GetFrameName(string name, int index)
    {
        if (index == 0)
            return name;

        int last = name.Length - FirstDigitScanOffset;
        while (last >= 0 && !char.IsAsciiDigit(name[last]))
        {
            last--;
        }

        if (last < 0)
            return string.Empty;

        int first = last;
        while (first - 1 >= 0 && char.IsAsciiDigit(name[first - 1]))
        {
            first--;
        }

        if (first - 1 >= 0 && name[first - 1] == '-')
        {
            first--;
        }

        Span<char> number = stackalloc char[MaxNumberLength];
        index.TryFormat(number, out int numberLength, default, CultureInfo.InvariantCulture);
        var buffer = name.Length <= StackNameLength ? stackalloc char[StackNameLength] : new char[name.Length];
        buffer = buffer[..name.Length];
        name.CopyTo(buffer);
        for (int position = first; position <= last; position++)
        {
            buffer[position] = position - first < numberLength ? number[position - first] : '\0';
        }

        int end = buffer.IndexOf('\0');
        return new string(end < 0 ? buffer : buffer[..end]);
    }
}

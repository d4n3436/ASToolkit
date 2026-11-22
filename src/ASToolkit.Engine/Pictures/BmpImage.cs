using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a picture read from BMP files, which hold uncompressed images with at least 8 bits for each pixel, with one frame in each file.
/// </summary>
public sealed class BmpImage : PictureImage
{
    /// <summary>
    /// Represents the extension of BMP files.
    /// </summary>
    public const string Extension = ".bmp";
    private const int MinDepth = 8;
    private const int RowAlignment = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="BmpImage"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public BmpImage(string gameFolder)
        : this(gameFolder, null)
    {
    }

    internal BmpImage(string gameFolder, GameLogger? logger)
        : base(gameFolder, logger)
    {
    }

    /// <summary>
    /// Opens the first BMP file, counts the frames in the numbered files after it and goes to the first frame.
    /// </summary>
    /// <param name="name">The path of the first file.</param>
    /// <returns><see langword="true"/> if the picture was loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x004284f0)]
    public override bool Load(string name)
    {
        if (!base.Load(name))
            return false;

        if (!ReadHeader(out var bmp))
            return false;

        CountFrames(name, Extension);
        Resize(bmp.Size, bmp.Depth / BitsPerByte);
        Rewind();
        return true;
    }

    /// <summary>
    /// Goes to the next frame, which is read from the next numbered file, and back to the first frame after the last one.
    /// </summary>
    [EngineRoutine(0x004286a0)]
    public override void Advance()
    {
        if (!HasPixels)
        {
            Report(LogError.Text, "Picture has not opened"u8, FrameIndex);
            return;
        }

        FrameIndex++;
        if (FrameIndex >= FrameCount)
        {
            Rewind();
            return;
        }

        CloseFile();
        Input = OpenFile(GetFrameName(Name, FrameIndex));
        if (!ReadHeader(out _))
            return;

        Input!.Seek(DataOffset, SeekOrigin.Begin);
        if (BytesPerPixel == 1)
        {
            var palette = MemoryMarshal.AsBytes(Palette);
            Input.ReadAtLeast(palette, palette.Length, false);
        }

        Input.Seek(DataOffset, SeekOrigin.Begin);
        ReadRows(-(Size.Width * BytesPerPixel) & RowAlignment - 1);
    }

    /// <summary>
    /// Reopens the first file and goes to the first frame.
    /// </summary>
    [EngineRoutine(0x00428620)]
    public override void Rewind()
    {
        if (!HasPixels)
            return;

        CloseFile();
        Input = OpenFile(Name);
        if (!ReadHeader(out _))
            return;

        Input!.Seek(DataOffset, SeekOrigin.Begin);
        FrameIndex = -1;
        Advance();
    }

    [EngineRoutine(0x00428400)]
    private bool ReadHeader(out BmpHeader bmp)
    {
        bmp = default;
        if (Input is null)
        {
            Report(LogError.Open, "bmp cadr"u8, FrameIndex);
            return false;
        }

        Input.ReadAtLeast(MemoryMarshal.AsBytes(new Span<BmpHeader>(ref bmp)), Unsafe.SizeOf<BmpHeader>(), false);
        if (bmp.Compression != 0)
        {
            Reset();
            Report(LogError.Invalid, "not supported bmp compression type"u8);
            return false;
        }

        if (bmp.Depth < MinDepth)
        {
            Reset();
            Report(LogError.Invalid, "not supported bmp 2 and 4 bit type"u8);
            return false;
        }

        DataOffset = bmp.DataOffset;
        if (!FitsPixels(bmp.Size, bmp.Depth))
        {
            Report(LogError.Text, "BMP parameters different from first cadr"u8, FrameIndex);
            return false;
        }

        return true;
    }
}

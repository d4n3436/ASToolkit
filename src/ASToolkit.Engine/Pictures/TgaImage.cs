using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a picture read from TGA files that hold uncompressed or run-length encoded images without a color map, with one frame in each file.
/// </summary>
public sealed class TgaImage : PictureImage
{
    /// <summary>
    /// Represents the extension of TGA files.
    /// </summary>
    public const string Extension = ".tga";
    private const int ColorMapType = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="TgaImage"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public TgaImage(string gameFolder)
        : this(gameFolder, null)
    {
    }

    internal TgaImage(string gameFolder, MyError? error)
        : base(gameFolder, error)
    {
    }

    /// <summary>
    /// Gets the image type of the open TGA file.
    /// </summary>
    public TgaImageType ImageType { get; private set; }

    /// <summary>
    /// Opens the first TGA file, counts the frames in the numbered files after it and goes to the first frame.
    /// </summary>
    /// <param name="name">The path of the first file.</param>
    /// <returns><see langword="true"/> if the picture was loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00428010)]
    public override bool Load(string name)
    {
        if (!base.Load(name))
            return false;

        if (!ReadHeader(out var tga))
            return false;

        FrameCount = 1;
        string stem = Name.EndsWith(Extension, StringComparison.Ordinal) ? Name[..^Extension.Length] : Name;
        if (stem.Length != 0 && IsDigit(stem[^1]))
        {
            while (GamePath.Resolve(GameFolder, GetFrameName(Name, FrameCount)) is not null)
            {
                FrameCount++;
            }
        }
        Resize(tga.Size, tga.Depth / BitsPerByte);
        Rewind();
        return true;
    }

    /// <summary>
    /// Goes to the next frame, read from the next numbered file, and back to the first frame after the last one.
    /// </summary>
    [EngineRoutine(0x004281d0)]
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
        if (!ImageType.HasFlag(TgaImageType.RunLength))
        {
            int rowSize = Size.Width * BytesPerPixel;
            for (int y = Size.Height - 1; y >= 0; y--)
            {
                Input!.ReadAtLeast(Pixels.Slice(y * rowSize, rowSize), rowSize, false);
            }

            return;
        }

        int width = Size.Width;
        int length = BytesPerPixel * Size.Height * width;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int read = Input!.ReadAtLeast(buffer.AsSpan(0, length), length, false);
            ReadOnlySpan<byte> bytes = buffer.AsSpan(0, read);
            int position = 0;
            for (int y = Size.Height - 1; y >= 0; y--)
            {
                int x = 0;
                while (x < width && position < bytes.Length)
                {
                    var packet = (TgaPacket)bytes[position++];
                    int count = (int)(packet & TgaPacket.CountMask) + 1;
                    bool isRun = packet.HasFlag(TgaPacket.Run);
                    for (int i = 0; i < count; i++)
                    {
                        int destination = (y * width + x + i) * BytesPerPixel;
                        if (destination + BytesPerPixel > Pixels.Length || position + BytesPerPixel > bytes.Length)
                            continue;

                        bytes.Slice(isRun ? position : position + i * BytesPerPixel, BytesPerPixel).CopyTo(Pixels.Slice(destination, BytesPerPixel));
                    }

                    position += isRun ? BytesPerPixel : count * BytesPerPixel;
                    x += count;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reopens the first file and goes to the first frame.
    /// </summary>
    [EngineRoutine(0x00428150)]
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

    [EngineRoutine(0x00427ef0)]
    private bool ReadHeader(out TgaHeader tga)
    {
        tga = default;
        if (Input is null)
        {
            Report(LogError.Open, "tga cadr"u8, FrameIndex);
            return false;
        }

        Input.ReadAtLeast(MemoryMarshal.AsBytes(new Span<TgaHeader>(ref tga)), Unsafe.SizeOf<TgaHeader>(), false);
        ImageType = tga.ImageType;
        DataOffset = tga.IdLength + Unsafe.SizeOf<TgaHeader>();
        if (ImageType == TgaImageType.None)
        {
            Reset();
            Report(LogError.Invalid, "not image in tga"u8);
            return false;
        }

        if (tga.ColorMapType == ColorMapType)
        {
            Reset();
            Report(LogError.Invalid, "not supported ColorMapType in tga"u8);
            return false;
        }

        if ((ImageType & TgaImageType.Grayscale) == TgaImageType.ColorMapped)
        {
            Reset();
            Report(LogError.Invalid, "not supported ColorMap in tga"u8);
            return false;
        }

        if (HasPixels && (Size != tga.Size || BytesPerPixel != tga.Depth / BitsPerByte))
        {
            Report(LogError.Text, "TGA parameters different from first cadr"u8, FrameIndex);
            return false;
        }

        return true;
    }

}

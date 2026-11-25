using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a picture read from a Z file, which holds a 16-bit depth value for each pixel, with the empty runs of every row compressed.
/// </summary>
public sealed class ZImage : PictureImage
{
    /// <summary>
    /// Represents the extension of Z files.
    /// </summary>
    public const string Extension = ".z";
    private const int Magic = 0x6675425a;
    private const int BytesPerDepth = 2;
    private const ushort EmptyDepth = 0x8000;
    private const int RunFlag = 0x8000;
    private const int RunMask = 0x7fff;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZImage"/> class.
    /// </summary>
    /// <param name="gameFolder">The game folder the picture files are in.</param>
    public ZImage(string gameFolder)
        : this(gameFolder, null)
    {
    }

    internal ZImage(string gameFolder, MyError? error)
        : base(gameFolder, error)
    {
    }

    /// <summary>
    /// Opens the Z file, reads its header and goes to the first frame.
    /// </summary>
    /// <param name="name">The path of the file.</param>
    /// <returns><see langword="true"/> if the picture was loaded; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00428810)]
    public override bool Load(string name)
    {
        if (!base.Load(name))
            return false;

        ZHeader z = default;
        Input!.ReadAtLeast(MemoryMarshal.AsBytes(new Span<ZHeader>(ref z)), Unsafe.SizeOf<ZHeader>(), false);
        if (z.Magic != Magic)
        {
            Reset();
            Report(LogError.Invalid, "Z format file"u8);
            return false;
        }

        FrameCount = z.Frames;
        DataOffset = Unsafe.SizeOf<ZHeader>();
        Speed = DefaultSpeed;
        Resize(z.Size, BytesPerDepth);
        Rewind();
        return true;
    }

    /// <summary>
    /// Goes to the next frame, which is read from the file, and back to the first frame after the last one.
    /// </summary>
    [EngineRoutine(0x004288c0)]
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
        ushort word = 0;
        for (int y = 0; y < Size.Height; y++)
        {
            for (int x = 0; x < Size.Width;)
            {
                Input!.ReadAtLeast(MemoryMarshal.AsBytes(new Span<ushort>(ref word)), sizeof(ushort), false);
                int count = word & RunMask;
                var row = Pixels.Slice((y * Size.Width + x) * BytesPerDepth, count * BytesPerDepth);
                if ((word & RunFlag) == 0)
                {
                    Input.ReadAtLeast(row, row.Length, false);
                }
                else
                {
                    MemoryMarshal.Cast<byte, ushort>(row).Fill(EmptyDepth);
                }

                x += count;
            }
        }
    }
}

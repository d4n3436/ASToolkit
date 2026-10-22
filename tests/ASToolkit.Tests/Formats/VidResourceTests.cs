using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class VidResourceTests
{
    private const VidFlags DataRequired = VidFlags.Runs8Bit | VidFlags.CurrentVersion;
    private const VidFlags RunLengthOptional = VidFlags.Blended | VidFlags.Depth | VidFlags.Palette;

    [Fact]
    public void GetFrame_PalettedCadr_SplitsRunsAtRowEndsAndOffsetsByTheRegion()
    {
        byte[] stream = [1, 2, 1, 2, 0, 3, 3, 4, 5, 0, 0];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette, VidPixelFormat.P8, 10, 10, BuildRecord(4, 3, 2, 1, stream)));

        Assert.Equal(
            [(3, 1, 1), (4, 1, 2), (5, 1, 3), (2, 2, 4), (3, 2, 5)],
            ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)).Select(pixel => (pixel.X, pixel.Y, (int)pixel.Color)).ToArray());
    }

    [Fact]
    public void GetFrame_CadrWithMesh_SkipsTheIndicesAndVertices()
    {
        byte[] stream = [0, 2, 7, 8, 0, 0];
        byte[] record = BuildRecord(2, 1, 0, 0, stream, indexCount: 6, vertexCount: 4);
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette, VidPixelFormat.P8, 4, 4, record));

        Assert.Equal([(0, 0, (ushort)7), (1, 0, (ushort)8)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrame_CadrWithFrameVector_ReadsTheHeaderAfterTheVector()
    {
        byte[] record = [.. new byte[12], .. BuildRecord(2, 1, 1, 2, [0, 2, 7, 8, 0, 0])];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette | VidFlags.FrameVector, VidPixelFormat.P8, 4, 4, record));

        Assert.Equal([(1, 2, (ushort)7), (2, 2, (ushort)8)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrameVector_CadrWithFrameVector_ReadsTheVectorOfTheFrameAnAliasRepeats()
    {
        byte[] vector = [.. BitConverter.GetBytes(1.5f), .. BitConverter.GetBytes(-2f), .. BitConverter.GetBytes(58f)];
        byte[] record = [.. vector, .. BuildRecord(2, 1, 1, 2, [0, 2, 7, 8, 0, 0])];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette | VidFlags.FrameVector, VidPixelFormat.P8, 4, 4, record, [0, 0, 0, 0]));

        Assert.Equal(new Vector3(1.5f, -2f, 58f), sprite.GetFrameVector(1));
    }

    [Fact]
    public void GetFrameVector_CadrWithoutFrameVector_ReturnsZero()
    {
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette, VidPixelFormat.P8, 4, 4, BuildRecord(2, 1, 1, 2, [0, 2, 7, 8, 0, 0])));

        Assert.Equal(Vector3.Zero, sprite.GetFrameVector(0));
    }

    [Fact]
    public void GetFrame_CadrWithBytesAfterTheTerminator_IgnoresThem()
    {
        byte[] stream = [0, 1, 9, 0, 0, 1, 2, 3, 4, 5];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette, VidPixelFormat.P8, 4, 4, BuildRecord(1, 1, 0, 0, stream)));

        Assert.Equal([(0, 0, (ushort)9)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrame_CadrRegionPastTheCanvas_ClipsColumnsAndRows()
    {
        byte[] stream = [0, 4, 1, 2, 3, 4, 0, 4, 5, 6, 7, 8, 0, 0];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette, VidPixelFormat.P8, 3, 1, BuildRecord(4, 2, 1, 0, stream)));

        Assert.Equal([(1, 0, (ushort)1), (2, 0, (ushort)2)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrame_CadrWithDepth_ReadsTheDepthAfterTheColor()
    {
        byte[] stream = [0, 2, 5, 0x34, 0x12, 6, 0x78, 0x56, 0, 0];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette | VidFlags.Depth, VidPixelFormat.P8, 4, 4, BuildRecord(2, 1, 0, 0, stream)));

        Assert.Equal([(0, 0, (ushort)0x1234), (1, 0, (ushort)0x5678)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetDepth(index)));
    }

    [Fact]
    public void GetFrame_CadrWithExtraByteAndDepth_ReadsTheDepthLast()
    {
        byte[] stream = [0, 1, 5, 0xAA, 0x34, 0x12, 0, 0];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette | VidFlags.Depth | VidFlags.SecondImageWide, VidPixelFormat.P8, 4, 4, BuildRecord(1, 1, 0, 0, stream)));

        Assert.Equal([(0, 0, (ushort)0x1234)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetDepth(index)));
        Assert.Equal([(0, 0, (ushort)5)], ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrame_CadrAlias_ReturnsThePixelsOfTheEarlierFrame()
    {
        byte[] pixels = BuildRecord(2, 1, 1, 1, [0, 2, 7, 8, 0, 0]);
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr | VidFlags.Palette, VidPixelFormat.P8, 4, 4, pixels, [0, 0, 0, 0]));

        Assert.Equal([(1, 1, (ushort)7), (2, 1, (ushort)8)], ReadPixels(sprite.GetFrame(1), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrame_CadrWithR5G6B5Format_ReadsSixteenBitColors()
    {
        byte[] stream = [0, 2, 0x00, 0xF8, 0xE0, 0x07, 2, 1, 0x1F, 0x00, 0, 0];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr, VidPixelFormat.R5G6B5, 4, 4, BuildRecord(3, 2, 0, 0, stream)));

        Assert.Equal(VidPixelFormat.R5G6B5, sprite.Format);
        Assert.Equal(2, sprite.GetFrame(0).PixelSize);
        Assert.Equal(
            [(0, 0, (ushort)0xF800), (1, 0, (ushort)0x07E0), (1, 1, (ushort)0x001F)],
            ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor(index)));
    }

    [Fact]
    public void GetFrame_CadrWithA8R8G8B8Format_ReadsThirtyTwoBitColors()
    {
        byte[] stream = [0, 1, 0x00, 0x00, 0xF8, 0xFF, 0, 1, 0xFF, 0xFF, 0xFF, 0x80, 0, 0];
        var sprite = new VidResource(BuildSprite(VidFlags.Cadr, VidPixelFormat.A8R8G8B8, 4, 4, BuildRecord(1, 2, 0, 0, stream)));

        Assert.Equal(4, sprite.GetFrame(0).PixelSize);
        Assert.Equal(
            [(0, 0, 0xFFF80000u), (0, 1, 0x80FFFFFFu)],
            ReadPixels(sprite.GetFrame(0), (run, index) => run.GetColor32(index)));
    }

    private static List<(int X, int Y, T Color)> ReadPixels<T>(VidFrame frame, Func<VidRun, int, T> read)
    {
        var pixels = new List<(int X, int Y, T Color)>();
        foreach (var run in frame)
        {
            for (int index = 0; index < run.Count; index++)
            {
                pixels.Add((run.X + index, run.Y, read(run, index)));
            }
        }

        return pixels;
    }

    private static byte[] BuildRecord(ushort width, ushort height, ushort left, ushort top, byte[] stream, ushort indexCount = 0, ushort vertexCount = 0)
    {
        byte[] record = new byte[12 + 2 * indexCount + 4 * vertexCount + stream.Length];
        Write(record, 0, indexCount);
        Write(record, 2, vertexCount);
        Write(record, 4, width);
        Write(record, 6, height);
        Write(record, 8, left);
        Write(record, 10, top);
        stream.CopyTo(record, 12 + 2 * indexCount + 4 * vertexCount);
        return record;
    }

    private static byte[] BuildSprite(VidFlags flags, VidPixelFormat format, ushort width, ushort height, params byte[][] cadrRecords)
    {
        bool paletted = flags.HasFlag(VidFlags.Palette);
        byte[] headRecord = new byte[14];
        Write(headRecord, 0x00, (ushort)flags);
        Write(headRecord, 0x04, (ushort)cadrRecords.Length);
        Write(headRecord, 0x06, width);
        Write(headRecord, 0x08, height);
        Write(headRecord, 0x0A, (uint)format);

        var buffer = new List<byte>(8);
        buffer.AddRange("RES\0"u8);
        buffer.AddRange(new byte[4]);
        buffer.AddRange("VID\0"u8);
        AddChunk(buffer, "HEAD"u8, SingleRecordBody(headRecord));
        if (paletted)
        {
            byte[] palette = new byte[1040];
            byte[] pal = new byte[8 + palette.Length];
            Write(pal, 0x04, (uint)palette.Length);
            AddChunk(buffer, "PAL "u8, pal);
        }

        AddChunk(buffer, "CADR"u8, RecordBody(cadrRecords));
        Write(buffer, 4, (uint)(buffer.Count - 8));
        return buffer.ToArray();
    }

    private static byte[] SingleRecordBody(byte[] record) => RecordBody([record]);

    private static byte[] RecordBody(byte[][] records)
    {
        var body = new List<byte>();
        body.AddRange(new byte[12]);
        Write(body, 0x00, 0x80000000u);
        Write(body, 0x08, (uint)records.Length);
        foreach (byte[] record in records)
        {
            body.AddRange(new byte[4]);
            Write(body, body.Count - 4, (uint)record.Length);
            body.AddRange(record);
        }

        return body.ToArray();
    }

    private static void AddChunk(List<byte> buffer, ReadOnlySpan<byte> tag, byte[] body)
    {
        buffer.AddRange(tag);
        buffer.AddRange(new byte[4]);
        int sizeOffset = buffer.Count - 4;
        buffer.AddRange(body);
        Write(buffer, sizeOffset, (uint)body.Length);
    }

    private static void Write<T>(byte[] data, int offset, T value) where T : unmanaged =>
        MemoryMarshal.Write(data.AsSpan(offset), in value);

    private static void Write<T>(List<byte> buffer, int offset, T value) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];
        MemoryMarshal.Write(bytes, in value);
        for (int i = 0; i < bytes.Length; i++)
        {
            buffer[offset + i] = bytes[i];
        }
    }

    [Fact]
    public void GetFrame_GameSprites_KeepEveryPixelInsideTheCanvas()
    {
        string folder = TestGameFolder.RequireFolder();
        int frames = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*.vid", SearchOption.AllDirectories))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!bytes.AsSpan().StartsWith("RES "u8))
            {
                continue;
            }

            var sprite = new VidResource(bytes);
            var flagBase = sprite.Flags & ~RunLengthOptional;
            if (flagBase != DataRequired && flagBase != VidFlags.Cadr)
            {
                continue;
            }

            Assert.Equal(sprite.HasPalette ? 1024 : 0, sprite.Palette.Length);
            for (int index = 0; index < sprite.FrameCount; index++)
            {
                var frame = sprite.GetFrame(index);
                int lastRow = frame.Top - 1;
                foreach (var run in frame)
                {
                    Assert.True(run is { Count: > 0, X: >= 0 } && run.X + run.Count <= sprite.Width, $"{path} frame {index} x {run.X} count {run.Count}");
                    Assert.True(run.Y >= lastRow && run.Y >= frame.Top && run.Y < frame.Top + frame.RowCount, $"{path} frame {index} row {run.Y}");
                    lastRow = run.Y;
                }

                frames++;
            }
        }

        Assert.True(frames > 0);
    }
}

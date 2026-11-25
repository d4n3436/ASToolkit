using System.Buffers;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const int MaxRunLength = 0xff;
    private const int BitsPerByte = 8;
    private const uint CrcPolynomial = 0xedb88320;
    private const int CrcTableSize = 256;
    private const int StackChecksumCount = 256;
    private const int MaxRecordSize = 0x200000;
    private const int DirectionCount = 8;
    private const int MaxOutlinePoints = 0xff;
    private const int OutlineDepthScale = 8;
    private const int OutlineDepthBias = 0x80;
    private const int FinerTolerance = 0;
    private const int CoarserTolerance = 3;
    private const int MatrixSize = PaletteSize * PaletteSize;
    private const uint HalfAlphaLimit = 0xef000000;
    private const int TileSize = 8;
    private const int TileNeighborhood = TileSize + 1;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static readonly Size[] Directions =
    [
        new(0, -1),
        new(1, -1),
        new(1, 0),
        new(1, 1),
        new(0, 1),
        new(-1, 1),
        new(-1, 0),
        new(-1, -1)
    ];

    /// <summary>
    /// Converts the picture into the bytes of a sprite file with a frame for every frame of the picture.
    /// </summary>
    /// <param name="options">How the sprite file is made.</param>
    /// <returns>The sprite file, or an empty memory if the picture doesn't have colors or alpha values, or if a hardware layer sprite file is asked for without depth values.</returns>
    [EngineRoutine(0x0042c8c0)]
    public ReadOnlyMemory<byte> Convert(PictureConvertOptions options = PictureConvertOptions.None)
    {
        if (options.HasFlag(PictureConvertOptions.HardwareLayer))
        {
            Flags |= PictureFlags.HardwareLayer;
        }
        else if (options.HasFlag(PictureConvertOptions.Hardware))
        {
            Flags |= PictureFlags.Hardware;
        }

        if (options.HasFlag(PictureConvertOptions.RangeCoded))
        {
            Flags |= PictureFlags.RangeCoded;
        }

        if (options.HasFlag(PictureConvertOptions.Outline))
        {
            Flags |= PictureFlags.Outline;
        }

        if (options.HasFlag(PictureConvertOptions.SurfaceVariant))
        {
            Flags |= PictureFlags.SurfaceVariant;
        }

        if (Colors.Image is { HasPixels: true, BytesPerPixel: 1 })
        {
            Flags |= PictureFlags.Palette;
        }

        if (Flags.HasFlag(PictureFlags.Hardware))
        {
            Flags &= ~PictureFlags.Palette;
        }

        if (!Colors.Image.HasPixels && !Alpha.Image.HasPixels)
        {
            Colors.Image.Report(LogError.Invalid, "not picture3"u8);
            return default;
        }

        if (!Flags.HasFlag(PictureFlags.Depth) && Flags.HasFlag(PictureFlags.HardwareLayer))
        {
            Colors.Image.Report(LogError.Text, "Unsupported files combination"u8);
            return default;
        }

        var builder = new ResContainerBuilder { Magic = "RES "u8.ToArray(), Kind = "VID "u8.ToArray() };
        Flags |= PictureFlags.CurrentVersion;
        var head = new VidHeader((VidType)(short)Flags, (short)Colors.Image.Speed, (short)Colors.Image.FrameCount, Colors.Image.Size);
        builder.Chunks.Add(CreateChunk("HEAD", [MemoryMarshal.AsBytes(new ReadOnlySpan<VidHeader>(in head)).ToArray()]));
        if (options.HasFlag(PictureConvertOptions.CoarseOutline))
        {
            Flags |= PictureFlags.CoarseOutline;
        }

        if (options.HasFlag(PictureConvertOptions.OutlineOnly))
        {
            Flags |= PictureFlags.OutlineOnly;
        }

        if (Flags.HasFlag(PictureFlags.HardwareLayer))
        {
            ConvertHardwareLayer(builder);
        }
        else if (Flags.HasFlag(PictureFlags.Light))
        {
            builder.Chunks.Add(CreateChunk("DATA", [ConvertLight()]));
        }
        else if (!Flags.HasFlag(PictureFlags.Hardware) || Flags.HasFlag(PictureFlags.Alpha) && Flags.HasFlag(PictureFlags.Depth))
        {
            ConvertSoftware(builder);
        }
        else
        {
            ConvertHardware(builder);
        }

        return builder.Build();
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[CrcTableSize];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < BitsPerByte; bit++)
            {
                value = (value & 1) != 0 ? value >> 1 ^ CrcPolynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    private static void Append<T>(byte[] buffer, ref int length, T value)
        where T : unmanaged
    {
        MemoryMarshal.Write(buffer.AsSpan(length), in value);
        length += Unsafe.SizeOf<T>();
    }

    private static void AddFrame(List<byte[]> frames, ref int savings, ReadOnlySpan<byte> body, RangeCoder? coder)
    {
        var record = new ResRecordBuilder();
        record.Write(body.Length);
        record.WriteBody(body, coder);
        frames.Add(record.Bytes.ToArray());
        savings += record.CompressionSavings;
    }

    private static ResChunkBuilder CreateChunk(string tag, List<byte[]> records, int savings = 0) => new() { Tag = tag, Flags = ResChunkFlags.ExtendedHeader, Records = records, CompressionSavings = savings };

    [EngineRoutine(0x00428eb0)]
    private uint GetChecksum()
    {
        uint crc = 0;
        var size = Colors.Image.Size;
        for (int x = 0; x < size.Width; x++)
        {
            for (int y = 0; y < size.Height; y++)
            {
                var point = new Point(x, y);
                uint value;
                if (Flags.HasFlag(PictureFlags.Depth))
                {
                    value = (uint)GetDepth(point);
                    crc = CrcTable[(crc & 0xff) ^ (byte)value] ^ crc >> BitsPerByte;
                    crc = CrcTable[(crc & 0xff) ^ (byte)(value >> BitsPerByte)] ^ crc >> BitsPerByte;
                }

                if (Flags.HasFlag(PictureFlags.Palette))
                {
                    Flags &= ~PictureFlags.Palette;
                    value = GetHighColor(point);
                    Flags |= PictureFlags.Palette;
                }
                else
                {
                    value = GetHighColor(point);
                }

                crc = CrcTable[(crc & 0xff) ^ (byte)value] ^ crc >> BitsPerByte;
                crc = CrcTable[(crc & 0xff) ^ (byte)(value >> BitsPerByte)] ^ crc >> BitsPerByte;
            }
        }

        return crc;
    }

    [EngineRoutine(0x0042ad80)]
    private int ConvertOutline(Span<byte> destination)
    {
        MemoryMarshal.Write(destination, new VidFrameHeader(0));
        if (!Flags.HasFlag(PictureFlags.Outline))
            return Unsafe.SizeOf<VidFrameHeader>();

        var size = Colors.Image.Size;

        int area = size.Width * size.Height;
        var path = ArrayPool<Point>.Shared.Rent(area);
        byte[] visited = ArrayPool<byte>.Shared.Rent(area);
        try
        {
            visited.AsSpan(0, area).Clear();
            while (true)
            {
                var origin = default(Point);
                bool isFound = false;
                for (int line = 0; line < size.Width; line++)
                {
                    for (int offset = 0; offset <= line; offset++)
                    {
                        var point = new Point(line, offset);
                        if (IsOpaque(point) && visited[size.Width * point.Y + point.X] == 0)
                        {
                            origin = point;
                            isFound = true;
                            break;
                        }
                    }

                    if (isFound)
                        break;

                    for (int offset = 0; offset < line; offset++)
                    {
                        var point = new Point(offset, line);
                        if (IsOpaque(point) && visited[size.Width * point.Y + point.X] == 0)
                        {
                            origin = point;
                            isFound = true;
                            break;
                        }
                    }

                    if (isFound)
                        break;
                }

                if (!isFound)
                    return Unsafe.SizeOf<VidFrameHeader>();

                path[0] = origin;
                int pathCount = 1;
                int back = 0;
                var position = origin;
                do
                {
                    int turn = (back + 1) & (DirectionCount - 1);
                    bool isMoved = false;
                    while (turn != back)
                    {
                        var next = position + Directions[turn];
                        if (IsOpaque(next) && visited[size.Width * next.Y + next.X] == 0)
                        {
                            position = next;
                            back = turn ^ 4;
                            path[pathCount++] = position;
                            isMoved = true;
                            break;
                        }

                        turn = (turn + 1) & (DirectionCount - 1);
                    }

                    if (isMoved)
                        continue;

                    pathCount--;
                    if (pathCount < 1)
                        break;

                    var last = path[pathCount];
                    var previous = path[pathCount - 1];
                    int found = 0;
                    while (found < DirectionCount && last != previous + Directions[found])
                    {
                        found++;
                    }

                    back = found ^ 4;
                }
                while (position != path[0]);

                int vertexCount = 1;
                int current = 0;
                int tolerance = Flags.HasFlag(PictureFlags.CoarseOutline) ? CoarserTolerance : FinerTolerance;
                while (current < pathCount)
                {
                    int end = current + 3;
                    for (; end < pathCount; end++)
                    {
                        var from = path[current];
                        var to = path[end];
                        if (from == to || Math.Abs(from.X - path[end - 1].Y - path[end - 1].X + from.Y) < 1)
                            continue;

                        bool isDeviating = false;
                        for (int between = current + 1; between < end; between++)
                        {
                            int rootValue = (from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y);
                            int rootBit = 0x40000000;
                            int length = 0;
                            do
                            {
                                int next = length >> 1;
                                if (rootBit + length <= rootValue)
                                {
                                    rootValue -= rootBit + length;
                                    next |= rootBit;
                                }

                                rootBit >>= 2;
                                length = next;
                            }
                            while (rootBit != 0);

                            var middle = path[between];
                            int cross = (to.Y - from.Y) * middle.X + middle.Y * (from.X - to.X) - from.X * to.Y + to.X * from.Y;
                            if (Math.Abs(cross) / length > tolerance)
                            {
                                isDeviating = true;
                                break;
                            }
                        }

                        if (isDeviating)
                            break;
                    }

                    current = end - 1;
                    if (end < pathCount)
                    {
                        path[vertexCount++] = path[end - 1];
                    }
                }

                if (vertexCount <= 2)
                {
                    visited[size.Width * origin.Y + origin.X] = 1;
                    continue;
                }

                while (vertexCount > MaxOutlinePoints)
                {
                    vertexCount /= 2;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        path[i] = path[2 * i];
                    }
                }

                MemoryMarshal.Write(destination, new VidFrameHeader((short)vertexCount));
                var points = MemoryMarshal.Cast<byte, VidOutlinePoint>(destination[Unsafe.SizeOf<VidFrameHeader>()..]);
                for (int i = 0; i < vertexCount; i++)
                {
                    var vertex = path[i];
                    int depthOffset = GetDepth(vertex);
                    depthOffset = depthOffset / OutlineDepthScale - OutlineDepthBias;
                    points[i] = new VidOutlinePoint((short)vertex.X, (short)(vertex.Y + (short)depthOffset), (short)depthOffset);
                }

                return Unsafe.SizeOf<VidFrameHeader>() + vertexCount * Unsafe.SizeOf<VidOutlinePoint>();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(visited);
            ArrayPool<Point>.Shared.Return(path);
        }
    }

    [EngineRoutine(0x0042b3a0)]
    private void QuantizePalette()
    {
        _paletteMap = new byte[PaletteMapSize];
        _palette[0] = 0;
        var size = Colors.Image.Size;
        int count = 1;
        Span<int> distances = stackalloc int[PaletteSize];
        int[] matrix = ArrayPool<int>.Shared.Rent(MatrixSize);
        try
        {
            for (int frame = 0; frame < Colors.Image.FrameCount; frame++)
            {
                for (int y = 0; y < size.Height; y++)
                {
                    for (int x = 0; x < size.Width; x++)
                    {
                        var point = new Point(x, y);
                        if (!IsOpaque(point))
                            continue;

                        uint color = GetColor(point);
                        if (_paletteMap[(int)((((color & 0xfc00 | color >> 3 & 0x1f0000) >> 2 | color & 0xf8) >> 3) * 0x10 + (color >> 28))] != 0)
                            continue;

                        int nearest = 0;
                        bool isMatched = false;
                        for (int entry = 0; entry < count; entry++)
                        {
                            uint other = _palette[entry];
                            int red = (int)(other >> 19 & 0x1f) - (int)(color >> 19 & 0x1f);
                            int blue = (int)(other >> 3 & 0x1f) - (int)(color >> 3 & 0x1f);
                            int alpha = (int)(other / 0xf000000) - (int)(color / 0xf000000);
                            int green = (int)(other >> 10 & 0x3f) - (int)(color >> 10 & 0x3f);
                            int distance = red * red * 0x36640 + blue * blue * 0x790 + alpha * alpha * 0x1a5e + green * green * 0x3840;
                            distances[entry] = distance;
                            if (distance == 0)
                            {
                                SetPaletteIndex(color, (byte)entry);
                                isMatched = true;

                                break;
                            }

                            if (distance < distances[nearest])
                            {
                                nearest = entry;
                            }
                        }

                        if (isMatched)
                            continue;

                        if (count < PaletteSize)
                        {
                            for (int entry = 0; entry < count; entry++)
                            {
                                matrix[count * PaletteSize + entry] = distances[entry];
                                matrix[entry * PaletteSize + count] = distances[entry];
                            }

                            _palette[count] = color;
                            SetPaletteIndex(color, (byte)count);
                            count++;
                            continue;
                        }

                        int row = 1;
                        int column = 0;
                        for (int first = 1; first < PaletteSize; first++)
                        {
                            for (int second = 0; second < first; second++)
                            {
                                if (matrix[first * PaletteSize + second] >= matrix[row * PaletteSize + column])
                                    continue;

                                row = first;
                                column = second;
                            }
                        }

                        if (matrix[row * PaletteSize + column] >= distances[nearest])
                        {
                            SetPaletteIndex(color, (byte)nearest);
                            continue;
                        }

                        SetPaletteIndex(_palette[column], (byte)nearest);
                        _palette[column] = color;
                        SetPaletteIndex(color, (byte)column);
                        for (int entry = 0; entry < PaletteSize; entry++)
                        {
                            matrix[column * PaletteSize + entry] = distances[entry];
                            matrix[entry * PaletteSize + column] = distances[entry];
                        }
                    }
                }

                Advance();
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(matrix);
        }

        Rewind();

        Span<int> counts = stackalloc int[PaletteSize];
        Span<int> reds = stackalloc int[PaletteSize];
        Span<int> greens = stackalloc int[PaletteSize];
        Span<int> blues = stackalloc int[PaletteSize];
        Span<int> alphas = stackalloc int[PaletteSize];
        for (int cell = 0; cell < PaletteMapSize; cell++)
        {
            int i = _paletteMap[cell];
            if (i == 0)
                continue;

            counts[i]++;
            int color = cell >> 4;
            reds[i] += color >> 8 & 0xf8;
            greens[i] += color >> 3 & 0xfc;
            blues[i] += (color & 0x1f) << 3;
            alphas[i] += (cell & 0xf) * 0x10 + 0xf;
        }

        for (int i = 0; i < PaletteSize; i++)
        {
            if (counts[i] == 0)
                continue;

            _palette[i] = Graphics.PackColor(alphas[i] / counts[i], reds[i] / counts[i], greens[i] / counts[i], blues[i] / counts[i]);
        }

        if (!Flags.HasFlag(PictureFlags.Alpha))
            return;

        for (int i = 0; i < count; i++)
        {
            uint color = _palette[i];
            if ((color & 0xff000000) > HalfAlphaLimit)
            {
                color |= 0xff000000;
            }
            else
            {
                color = color & 0xffffff | Math.Min((color >> 28) << 4, MaxChannel) << AlphaShift;
            }

            _palette[i] = color;
            if ((color & 0xff000000) == 0)
                continue;

            uint alpha = color >> AlphaShift;
            uint blue = Math.Min((color & ChannelMask) * MaxChannel / alpha, MaxChannel);
            uint green = Math.Min((color >> 8 & ChannelMask) * MaxChannel / alpha, MaxChannel);
            uint red = Math.Min((color >> 16 & ChannelMask) * MaxChannel / alpha, MaxChannel);
            _palette[i] = Graphics.PackColor((int)alpha, (int)red, (int)green, (int)blue);
        }
    }

    [EngineRoutine(0x0042bae0)]
    private void ConvertSoftware(ResContainerBuilder builder)
    {
        var coder = Flags.HasFlag(PictureFlags.RangeCoded) ? new RangeCoder(1) : null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxRecordSize);
        try
        {
            if (Flags.HasFlag(PictureFlags.Palette))
            {
                var record = new ResRecordBuilder();
                if (!Flags.HasFlag(PictureFlags.CurrentVersion))
                {
                    Span<VidPackedColor> packed = stackalloc VidPackedColor[PaletteSize];
                    for (int i = 0; i < PaletteSize; i++)
                    {
                        packed[i] = new VidPackedColor(Colors.Image.Palette[i]);
                    }

                    record.Write(MemoryMarshal.AsBytes(packed));
                }
                else if (!Flags.HasFlag(PictureFlags.Alpha))
                {
                    record.Write(MemoryMarshal.AsBytes(Colors.Image.Palette));
                }
                else
                {
                    QuantizePalette();
                    record.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(_palette)));
                }

                builder.Chunks.Add(CreateChunk("PAL ", [record.Bytes.ToArray()]));
            }

            var frames = new List<byte[]>();
            int savings = 0;
            int frameCount = Colors.Image.FrameCount;
            var checksums = frameCount <= StackChecksumCount ? stackalloc uint[StackChecksumCount] : new uint[frameCount];
            checksums = checksums[..frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                int length = 0;
                int match = FindRepeatedFrame(checksums, frame);
                if (match >= 0)
                {
                    Append(buffer, ref length, (short)match);
                    AddFrame(frames, ref savings, buffer.AsSpan(0, length), coder);
                    Advance();
                    continue;
                }

                length = ConvertOutline(buffer);
                var bounds = GetBounds(true, false);
                int top = bounds.Top;
                int bottom = bounds.Bottom;
                if (Flags.HasFlag(PictureFlags.OutlineOnly))
                {
                    top = 0;
                    bottom = 0;
                }

                Append(buffer, ref length, new VidRowsHeader((short)top, (short)(bottom - top)));
                bool isDepth = Flags.HasFlag(PictureFlags.Depth);
                bool isPalette = Flags.HasFlag(PictureFlags.Palette);
                for (int y = top; y < bottom; y++)
                {
                    int x = 0;
                    while (true)
                    {
                        int start = x;
                        while (!IsOpaque(new Point(x, y)))
                        {
                            x++;
                            if (x >= Colors.Image.Size.Width)
                                break;
                        }

                        if (x >= Colors.Image.Size.Width)
                            break;

                        for (; x - start > MaxRunLength; start += MaxRunLength)
                        {
                            Append(buffer, ref length, new VidRunHeader(MaxRunLength, 0));
                        }

                        byte skip = (byte)(x - start);
                        int runStart = x;
                        int lookahead = x + 2;
                        do
                        {
                            if (!IsOpaque(new Point(x, y)) && (!isDepth || !IsOpaque(new Point(lookahead - 1, y)) && !IsOpaque(new Point(lookahead, y))))
                                break;

                            x++;
                            lookahead++;
                        }
                        while (x - runStart < MaxRunLength);

                        Append(buffer, ref length, new VidRunHeader(skip, (byte)(x - runStart)));
                        if (isDepth)
                        {
                            for (int i = runStart; i < x; i++)
                            {
                                Append(buffer, ref length, GetDepth(new Point(i, y)));
                            }
                        }

                        for (int i = runStart; i < x; i++)
                        {
                            if (isPalette)
                            {
                                buffer[length++] = (byte)GetHighColor(new Point(i, y));
                                continue;
                            }

                            Append(buffer, ref length, (short)GetHighColor(new Point(i, y)));
                        }
                    }

                    Append(buffer, ref length, new VidRunHeader(0, 0));
                }

                AddFrame(frames, ref savings, buffer.AsSpan(0, length), coder);
                Advance();
            }

            if (frameCount > 0)
            {
                builder.Chunks.Add(CreateChunk("DATA", frames, savings));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [EngineRoutine(0x0042c070)]
    private void ConvertHardwareLayer(ResContainerBuilder builder)
    {
        int frameCount = Colors.Image.FrameCount;
        var coder = Flags.HasFlag(PictureFlags.RangeCoded) ? new RangeCoder(1) : null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxRecordSize);
        try
        {
            var checksums = frameCount <= StackChecksumCount ? stackalloc uint[StackChecksumCount] : new uint[frameCount];
            checksums = checksums[..frameCount];
            if (Flags.HasFlag(PictureFlags.Alpha))
            {
                Flags &= ~PictureFlags.Palette;
            }

            if (Flags.HasFlag(PictureFlags.Palette))
            {
                var palette = new ResRecordBuilder();
                palette.Write(MemoryMarshal.AsBytes(Colors.Image.Palette));
                builder.Chunks.Add(CreateChunk("PAL ", [palette.Bytes.ToArray()]));
            }

            var frames = new List<byte[]>();
            int savings = 0;
            for (int frame = 0; frame < frameCount; frame++)
            {
                int length = 0;
                int match = FindRepeatedFrame(checksums, frame);
                if (match >= 0)
                {
                    Append(buffer, ref length, match);
                    AddFrame(frames, ref savings, buffer.AsSpan(0, length), coder);
                    Advance();
                    continue;
                }

                var bounds = GetBounds(true, true);
                VidPixelFormat format;
                if (Flags.HasFlag(PictureFlags.Palette))
                {
                    format = VidPixelFormat.P8;
                }
                else if (Flags.HasFlag(PictureFlags.Alpha))
                {
                    format = VidPixelFormat.A8R8G8B8;
                }
                else
                {
                    format = VidPixelFormat.A1R5G5B5;
                }

                MemoryMarshal.Write(buffer, new VidLayerHeader(format, (short)bounds.Width, (short)bounds.Height));
                length = Unsafe.SizeOf<VidLayerHeader>();
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    for (int x = bounds.Left; x < bounds.Right; x++)
                    {
                        if (Flags.HasFlag(PictureFlags.Palette))
                        {
                            buffer[length++] = (byte)GetHighColor(new Point(x, y));
                            continue;
                        }

                        if (Flags.HasFlag(PictureFlags.Alpha))
                        {
                            Append(buffer, ref length, (int)GetHighColor(new Point(x, y)));
                            continue;
                        }

                        Append(buffer, ref length, (short)GetHighColor(new Point(x, y)));
                    }
                }

                int meshStart = length;
                length += Unsafe.SizeOf<VidLayerMeshHeader>();
                int tileCount = 0;
                int indexCount = 0;
                int stride = bounds.Width + TileNeighborhood;
                short[] grid = new short[stride * (bounds.Height + TileNeighborhood)];
                for (int y = bounds.Top; y < bounds.Bottom + TileSize; y += TileSize)
                {
                    for (int x = bounds.Left; x < bounds.Right + TileSize; x += TileSize)
                    {
                        var area = new Rectangle(x - TileSize, y - TileSize, TileNeighborhood, TileNeighborhood);
                        if (!HasOpaque(area))
                            continue;

                        short depth = GetRegionDepth(area);
                        Append(buffer, ref length, new VidLayerVertex((short)x, (short)y, depth, (short)(x - bounds.Left), (short)(y - bounds.Top)));
                        grid[(y - bounds.Top) * stride - bounds.Left + x] = (short)tileCount;
                        tileCount++;
                    }
                }

                for (int y = bounds.Top; y < bounds.Bottom + TileSize; y += TileSize)
                {
                    for (int x = bounds.Left; x < bounds.Right + TileSize; x += TileSize)
                    {
                        if (!HasOpaque(new Rectangle(x, y, TileSize, TileSize)))
                            continue;

                        int upper = (y - bounds.Top) * stride - bounds.Left + x;
                        int lower = (y - bounds.Top + TileSize) * stride - bounds.Left + x;
                        ReadOnlySpan<short> corners = [grid[upper], grid[upper + TileSize], grid[lower], grid[lower + TileSize], grid[upper + TileSize], grid[lower]];
                        MemoryMarshal.AsBytes(corners).CopyTo(buffer.AsSpan(length));
                        length += corners.Length * sizeof(short);
                        indexCount += corners.Length;
                    }
                }

                MemoryMarshal.Write(buffer.AsSpan(meshStart), new VidLayerMeshHeader(tileCount, indexCount));

                AddFrame(frames, ref savings, buffer.AsSpan(0, length), coder);
                Advance();
            }

            if (frameCount > 0)
            {
                builder.Chunks.Add(CreateChunk("DATA", frames, savings));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private int FindRepeatedFrame(Span<uint> checksums, int frame)
    {
        checksums[frame] = GetChecksum();
        return checksums[..frame].IndexOf(checksums[frame]);
    }

    [EngineRoutine(0x0042c730)]
    private byte[] ConvertLight()
    {
        byte[] bytes = new byte[Colors.Image.FrameCount * sizeof(uint)];
        var colors = MemoryMarshal.Cast<byte, uint>(bytes);
        for (int i = 0; i < Colors.Image.FrameCount; i++)
        {
            colors[i] = GetColor(Point.Empty);
            Advance();
        }

        return bytes;
    }
}

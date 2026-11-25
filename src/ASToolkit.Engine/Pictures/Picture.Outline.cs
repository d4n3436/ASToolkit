using System.Buffers;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const int DirectionCount = 8;
    private const int MaxOutlinePoints = 0xff;
    private const int OutlineDepthScale = 8;
    private const int OutlineDepthBias = 0x80;
    private const int FinerTolerance = 0;
    private const int CoarserTolerance = 3;

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

    private static int GetIntegerSquareRoot(int value)
    {
        int bit = 0x40000000;
        int result = 0;
        do
        {
            int next = result >> 1;
            if (bit + result <= value)
            {
                value -= bit + result;
                next |= bit;
            }

            bit >>= 2;
            result = next;
        }
        while (bit != 0);
        return result;
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
                if (!TryFindOutlineStart(visited, out var origin))
                    return Unsafe.SizeOf<VidFrameHeader>();

                int pathCount = TraceOutline(path, visited, origin);
                int vertexCount = SimplifyOutline(path, pathCount);
                if (vertexCount > 2)
                    return WriteOutline(destination, path, vertexCount);

                visited[size.Width * origin.Y + origin.X] = 1;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(visited);
            ArrayPool<Point>.Shared.Return(path);
        }
    }

    private bool TryFindOutlineStart(ReadOnlySpan<byte> visited, out Point origin)
    {
        var size = Colors.Image.Size;
        for (int line = 0; line < size.Width; line++)
        {
            for (int offset = 0; offset <= line; offset++)
            {
                var point = new Point(line, offset);
                if (IsOpaque(point) && visited[size.Width * point.Y + point.X] == 0)
                {
                    origin = point;
                    return true;
                }
            }

            for (int offset = 0; offset < line; offset++)
            {
                var point = new Point(offset, line);
                if (IsOpaque(point) && visited[size.Width * point.Y + point.X] == 0)
                {
                    origin = point;
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    private int TraceOutline(Span<Point> path, ReadOnlySpan<byte> visited, Point origin)
    {
        int width = Colors.Image.Size.Width;
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
                if (IsOpaque(next) && visited[width * next.Y + next.X] == 0)
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
        return pathCount;
    }

    private int SimplifyOutline(Span<Point> path, int pathCount)
    {
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
                    int length = GetIntegerSquareRoot((from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y));
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

        return vertexCount;
    }

    private int WriteOutline(Span<byte> destination, Span<Point> path, int vertexCount)
    {
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
            int offset = GetDepth(vertex);
            offset = offset / OutlineDepthScale - OutlineDepthBias;
            points[i] = new VidOutlinePoint((short)vertex.X, (short)(vertex.Y + (short)offset), (short)offset);
        }

        return Unsafe.SizeOf<VidFrameHeader>() + vertexCount * Unsafe.SizeOf<VidOutlinePoint>();
    }
}

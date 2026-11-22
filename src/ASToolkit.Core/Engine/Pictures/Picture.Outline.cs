using System.Buffers;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Engine;

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

    [EngineRoutine(0x0042ad80)]
    private int ConvertOutline(Span<byte> destination)
    {
        var output = MemoryMarshal.Cast<byte, short>(destination);
        output[0] = 0;
        if (!Flags.HasFlag(PictureFlags.Outline))
            return sizeof(short);

        var size = Colors.Image.Size;

        int area = size.Width * size.Height;
        var path = ArrayPool<Point>.Shared.Rent(area);
        var visited = ArrayPool<byte>.Shared.Rent(area);
        try
        {
            visited.AsSpan(0, area).Clear();
            while (true)
            {
                Point origin = default;
                bool isFound = false;
                for (int line = 0; line < size.Width && !isFound; line++)
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
                }

                if (!isFound)
                    return sizeof(short);

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

                if (vertexCount > 2)
                {
                    while (vertexCount > MaxOutlinePoints)
                    {
                        vertexCount /= 2;
                        for (int i = 0; i < vertexCount; i++)
                        {
                            path[i] = path[2 * i];
                        }
                    }

                    output[0] = (short)vertexCount;
                    int position16 = 1;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        var vertex = path[i];
                        int offset = (short)GetDepth(vertex);
                        offset = offset / OutlineDepthScale - OutlineDepthBias;
                        output[position16++] = (short)vertex.X;
                        output[position16++] = (short)(vertex.Y + (short)offset);
                        output[position16++] = (short)offset;
                    }

                    return position16 * sizeof(short);
                }

                visited[size.Width * origin.Y + origin.X] = 1;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(visited);
            ArrayPool<Point>.Shared.Return(path);
        }
    }

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
}

using System.Drawing;
using System.Runtime.CompilerServices;

namespace ASToolkit.Engine;

internal static class ScreenPrimitives
{
    private const int MaxChannel = byte.MaxValue;
    private const uint OpaqueAlpha = 0xff000000;
    private const int AlphaShift = 24;
    private const int RedShift = 16;
    private const int GreenShift = 8;

    public static void Blend(Screen screen, Point point, uint source, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        if (!screen.Clip.Contains(point) || point.X < 0 || point.Y < 0 || point.X >= screen.Size.Width || point.Y >= screen.Size.Height)
            return;

        new ScreenWriter(screen).Blend(point.Y * screen.Pitch + point.X, source, sourceFactor, destinationFactor);
    }

    public static void FillRectangle(Screen screen, Rectangle rectangle, uint color, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        var area = Rectangle.Intersect(rectangle, screen.Clip);
        area.Intersect(new Rectangle(Point.Empty, screen.Size));
        var writer = new ScreenWriter(screen);
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int row = y * screen.Pitch;
            for (int x = area.Left; x < area.Right; x++)
            {
                writer.Blend(row + x, color, sourceFactor, destinationFactor);
            }
        }
    }

    public static void DrawLines(Screen screen, ReadOnlySpan<WeatherVertex> vertices)
    {
        for (int i = 0; i + 1 < vertices.Length; i += 2)
        {
            var start = vertices[i];
            var end = vertices[i + 1];
            DrawLine(screen, new PointF(start.Position.X, start.Position.Y), start.Color, new PointF(end.Position.X, end.Position.Y), end.Color, BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
    }

    public static void DrawLine(Screen screen, PointF start, uint startColor, PointF end, uint endColor, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        float deltaX = end.X - start.X;
        float deltaY = end.Y - start.Y;
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(deltaX), MathF.Abs(deltaY)));

        for (int step = 0; step < steps; step++)
        {
            float share = (float)step / steps;
            var point = new Point((int)MathF.Floor(start.X + deltaX * share + 0.5f), (int)MathF.Floor(start.Y + deltaY * share + 0.5f));
            uint color = (uint)Lerp(startColor, endColor, AlphaShift, share) << AlphaShift | (uint)Lerp(startColor, endColor, RedShift, share) << RedShift |
                (uint)Lerp(startColor, endColor, GreenShift, share) << GreenShift | (uint)Lerp(startColor, endColor, 0, share);
            Blend(screen, point, color, sourceFactor, destinationFactor);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Combine(uint source, uint destination, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        if (sourceFactor == BlendFactor.One && destinationFactor == BlendFactor.Zero)
            return source | OpaqueAlpha;

        if (sourceFactor == BlendFactor.DestinationColor && destinationFactor == BlendFactor.One)
        {
            return OpaqueAlpha | (uint)AddColor((int)(source >> RedShift & MaxChannel), (int)(destination >> RedShift & MaxChannel)) << RedShift |
                (uint)AddColor((int)(source >> GreenShift & MaxChannel), (int)(destination >> GreenShift & MaxChannel)) << GreenShift |
                (uint)AddColor((int)(source & MaxChannel), (int)(destination & MaxChannel));
        }

        int alpha = (int)(source >> AlphaShift);
        return OpaqueAlpha | (uint)CombineChannel((int)(source >> RedShift & MaxChannel), alpha, (int)(destination >> RedShift & MaxChannel), sourceFactor, destinationFactor) << RedShift |
            (uint)CombineChannel((int)(source >> GreenShift & MaxChannel), alpha, (int)(destination >> GreenShift & MaxChannel), sourceFactor, destinationFactor) << GreenShift |
            (uint)CombineChannel((int)(source & MaxChannel), alpha, (int)(destination & MaxChannel), sourceFactor, destinationFactor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AddColor(int source, int destination) => Math.Clamp((source * destination + destination * MaxChannel + MaxChannel / 2) / MaxChannel, 0, MaxChannel);

    private static int Lerp(uint from, uint to, int shift, float share)
    {
        int first = (int)(from >> shift & MaxChannel);
        return (int)(first + ((int)(to >> shift & MaxChannel) - first) * share + 0.5f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CombineChannel(int source, int sourceAlpha, int destination, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        int result = source * GetFactor(sourceFactor, source, sourceAlpha, destination) + destination * GetFactor(destinationFactor, source, sourceAlpha, destination);

        return Math.Clamp((result + MaxChannel / 2) / MaxChannel, 0, MaxChannel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetFactor(BlendFactor factor, int sourceChannel, int sourceAlpha, int destinationChannel) => factor switch
    {
        BlendFactor.Zero => 0,
        BlendFactor.One => MaxChannel,
        BlendFactor.SourceColor => sourceChannel,
        BlendFactor.InverseSourceColor => MaxChannel - sourceChannel,
        BlendFactor.SourceAlpha => sourceAlpha,
        BlendFactor.InverseSourceAlpha => MaxChannel - sourceAlpha,
        BlendFactor.DestinationAlpha => MaxChannel,
        BlendFactor.InverseDestinationAlpha => 0,
        BlendFactor.DestinationColor => destinationChannel,
        BlendFactor.InverseDestinationColor => MaxChannel - destinationChannel,
        _ => throw new ArgumentOutOfRangeException(nameof(factor))
    };
}

using System.Drawing;

namespace ASToolkit.Core.Engine;

internal static class ScreenPrimitives
{
    private const int MaxChannel = byte.MaxValue;

    public static Color GetPixel(Screen screen, Point point)
    {
        int i = point.Y * screen.Pitch + point.X;
        uint color = screen.IsTrueColor ? screen.TrueColorPixels[i] : screen.Format.Expand(screen.HighColorPixels[i]);

        return Color.FromArgb((int)(color | 0xff000000));
    }

    public static void SetPixel(Screen screen, Point point, Color color)
    {
        int i = point.Y * screen.Pitch + point.X;
        uint packed = (uint)color.ToArgb() | 0xff000000;

        if (screen.IsTrueColor)
        {
            screen.TrueColorPixels[i] = packed;

            return;
        }

        screen.HighColorPixels[i] = screen.Format.Pack(packed);
    }

    public static void Blend(Screen screen, Point point, Color source, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        if (!screen.Clip.Contains(point) || point.X < 0 || point.Y < 0 || point.X >= screen.Size.Width || point.Y >= screen.Size.Height)
            return;

        var destination = GetPixel(screen, point);
        SetPixel(screen, point, Color.FromArgb(
            BlendChannel(source.R, source.A, destination.R, destination.A, sourceFactor, destinationFactor),
            BlendChannel(source.G, source.A, destination.G, destination.A, sourceFactor, destinationFactor),
            BlendChannel(source.B, source.A, destination.B, destination.A, sourceFactor, destinationFactor)));
    }

    public static void FillRectangle(Screen screen, Rectangle rectangle, Color color, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        var area = Rectangle.Intersect(rectangle, screen.Clip);
        area.Intersect(new Rectangle(Point.Empty, screen.Size));

        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                Blend(screen, new Point(x, y), color, sourceFactor, destinationFactor);
            }
        }
    }

    public static void DrawLines(Screen screen, ReadOnlySpan<WeatherVertex> vertices)
    {
        for (int i = 0; i + 1 < vertices.Length; i += 2)
        {
            var start = vertices[i];
            var end = vertices[i + 1];
            DrawLine(screen, new PointF(start.Position.X, start.Position.Y), Color.FromArgb((int)start.Color), new PointF(end.Position.X, end.Position.Y), Color.FromArgb((int)end.Color), BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
    }

    public static void DrawLine(Screen screen, PointF start, Color startColor, PointF end, Color endColor, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        float deltaX = end.X - start.X;
        float deltaY = end.Y - start.Y;
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(deltaX), MathF.Abs(deltaY)));

        for (int step = 0; step < steps; step++)
        {
            float share = (float)step / steps;
            var point = new Point((int)MathF.Floor(start.X + deltaX * share + 0.5f), (int)MathF.Floor(start.Y + deltaY * share + 0.5f));
            var color = Color.FromArgb(
                Lerp(startColor.A, endColor.A, share),
                Lerp(startColor.R, endColor.R, share),
                Lerp(startColor.G, endColor.G, share),
                Lerp(startColor.B, endColor.B, share));
            Blend(screen, point, color, sourceFactor, destinationFactor);
        }
    }

    private static int Lerp(int from, int to, float share) => (int)(from + (to - from) * share + 0.5f);

    public static int BlendChannel(int source, int sourceAlpha, int destination, int destinationAlpha, BlendFactor sourceFactor, BlendFactor destinationFactor)
    {
        int result = source * GetFactor(sourceFactor, source, sourceAlpha, destination, destinationAlpha)
            + destination * GetFactor(destinationFactor, source, sourceAlpha, destination, destinationAlpha);

        return Math.Clamp((result + MaxChannel / 2) / MaxChannel, 0, MaxChannel);
    }

    private static int GetFactor(BlendFactor factor, int sourceChannel, int sourceAlpha, int destinationChannel, int destinationAlpha) => factor switch
    {
        BlendFactor.Zero => 0,
        BlendFactor.One => MaxChannel,
        BlendFactor.SourceColor => sourceChannel,
        BlendFactor.InverseSourceColor => MaxChannel - sourceChannel,
        BlendFactor.SourceAlpha => sourceAlpha,
        BlendFactor.InverseSourceAlpha => MaxChannel - sourceAlpha,
        BlendFactor.DestinationAlpha => destinationAlpha,
        BlendFactor.InverseDestinationAlpha => MaxChannel - destinationAlpha,
        BlendFactor.DestinationColor => destinationChannel,
        BlendFactor.InverseDestinationColor => MaxChannel - destinationChannel,
        _ => throw new ArgumentOutOfRangeException(nameof(factor))
    };
}

using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const float MaxLineCoordinate = 10000;
    private const uint DebugTextColor = 0xff00ff00;

    [EngineRoutine(0x0041eba0)]
    internal void SetPixel(Vector2 point, uint color)
    {
        if (Screen is null)
            return;

        var viewport = Screen.Viewport;
        if (!(viewport.Left <= point.X && point.X < viewport.Right && viewport.Top <= point.Y && point.Y < viewport.Bottom))
            return;

        LockDepth();
        int index = Screen.Pitch * (int)point.Y + (int)point.X;
        if (Screen.IsTrueColor)
        {
            Screen.TrueColorPixels[index] = color;
        }
        else
        {
            Screen.HighColorPixels[index] = Screen.Format.Pack(color);
        }
    }

    [EngineRoutine(0x00433da0)]
    internal void Box(Map map, Vector2 topLeft, Vector2 bottomRight, uint color)
    {
        WuLine(map, topLeft, bottomRight with { Y = topLeft.Y }, color);
        WuLine(map, topLeft with { Y = bottomRight.Y }, bottomRight, color);
        WuLine(map, topLeft, topLeft with { Y = bottomRight.Y }, color);
        WuLine(map, bottomRight with { Y = topLeft.Y }, bottomRight, color);
    }

    [EngineRoutine(0x00433e10)]
    internal void WuLine(Map map, Vector2 start, Vector2 end, uint color)
    {
        start.X = ClampLineCoordinate(map, start.X, "x in Line"u8);
        end.X = ClampLineCoordinate(map, end.X, "x1 in Line"u8);
        start.Y = ClampLineCoordinate(map, start.Y, "y in Line"u8);
        end.Y = ClampLineCoordinate(map, end.Y, "y1 in Line"u8);
        int main = (int)start.X;
        int minor = (int)start.Y;
        int mainStep = 1;
        int minorStep = -1;
        if (end.X <= start.X)
        {
            mainStep = -1;
        }

        if (start.Y < end.Y)
        {
            minorStep = 1;
        }

        int width = Math.Abs((int)(end.X - start.X));
        int height = Math.Abs((int)(end.Y - start.Y));
        int minorLength = height;
        int majorLength = width;
        bool isSteep = width < height;
        if (isSteep)
        {
            minorLength = width;
            majorLength = height;
            (main, minor) = (minor, main);
            (mainStep, minorStep) = (minorStep, mainStep);
        }

        int error = -majorLength;
        for (int count = majorLength; count != 0; count--)
        {
            error += minorLength * 2;
            SetPixel(isSteep ? new Vector2(minor, main) : new Vector2(main, minor), color);

            for (; error >= 0; error -= majorLength * 2)
            {
                minor += minorStep;
            }

            main += mainStep;
        }

        SetPixel(end, color);
    }

    [EngineRoutine(0x00434420)]
    internal void DrawFormattedText(Vector2 position, string text) => DrawDeviceText(position, DebugTextColor, GameTextEncoding.Cyrillic.GetBytes(text));

    private static float ClampLineCoordinate(Map map, float value, ReadOnlySpan<byte> name)
    {
        if (!(MathF.Abs(value) > MaxLineCoordinate))
            return value;

        map.Session.Error.Error(GraphicsLogTag, LogError.Invalid, 0, name);

        return 0;
    }
}

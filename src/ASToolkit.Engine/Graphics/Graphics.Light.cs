using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const int LightStep = 4;
    private const int LightCells = 3;
    private const int LightLimit = 0x200;
    private const int LightArea = 500;
    private const int LightLift = 8;
    private const int LightFarOffset = 3;
    private const int LightNoDepth = 0x7fff;
    private const int LightIntensities = 256;
    private const int DepthShift = 3;
    private const float LightDepthBias = 50;
    private const float LightDepthScale = 0.00012207031f;
    private const float LightDepthBase = 0.015625f;
    private const int DepthBase = 0x80;

    /// <summary>
    /// Draws an omni-light, blended over the colors underneath.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="position">The position of the light relative to the view, with the height in the third component.</param>
    /// <param name="size">The width and depth of the light.</param>
    /// <param name="color">The color of the light, packed as red, green and blue.</param>
    /// <param name="isDoubled">Whether the intensity is doubled before it is blended.</param>
    [EngineRoutine(0x00433670)]
    public void DrawLightSource(Screen screen, Vector3 position, Vector2 size, uint color, bool isDoubled)
    {
        screen.LightPhase ^= 1;
        if ((color & ColorMask) == 0)
            return;

        int sizeX = (int)size.X;
        int width = sizeX / 2 * LightCells & ~(LightStep - 1);
        int sizeY = (int)size.Y;
        int height = sizeY / 2 * LightCells & ~(LightStep - 1);
        if (width > LightLimit)
        {
            width = LightLimit;
        }

        if (height > LightLimit)
        {
            height = LightLimit;
        }

        int area = sizeX * sizeY / LightArea;
        int z = (int)position.Z;
        int lift = z * 2 / LightCells + LightLift;
        float x = position.X;
        float y = position.Y - (lift - position.Z);
        var viewport = Viewport;
        if (viewport.Left > width + x || !(x - width < viewport.Right) || viewport.Top > height + y || !(y - height < viewport.Bottom))
            return;

        var destination = Rectangle.FromLTRB((int)(x - width), (int)(y - height), (int)(width + x), (int)(height + y));
        int depthPitch = screen.DepthWidth;
        var depth = screen.Depth;
        int pitch = screen.ScratchSize.Width;
        var texture = screen.LightBuffer;
        var format = screen.LightFormat;
        var colors = screen.ColorTable;
        bool isPalette = format == VidPixelFormat.P8;
        int originX = (int)x;
        int originY = (int)y;
        int row = 0;
        for (int offsetY = -height; offsetY < height; offsetY += LightStep, row += LightStep)
        {
            int col = 0;
            for (int offsetX = -width; offsetX < width; offsetX += LightStep, col += LightStep)
            {
                float lineY = offsetY + y;
                float lineX = offsetX + x;
                int near = SampleLightDepth(viewport, depth, new PointF(lineX, lineY), (originY + offsetY) * depthPitch + originX + offsetX);
                int far = SampleLightDepth(viewport, depth, new PointF(lineX + LightFarOffset, lineY + LightFarOffset), (originY + LightFarOffset + offsetY) * depthPitch + originX + offsetX + LightFarOffset);
                int sampled = Math.Min(near, far);
                if (sampled == LightNoDepth)
                    continue;

                int behind = (sampled - z) * (sampled - z);

                int distance = behind / 4 + (sampled - lift + offsetY) * (sampled - lift + offsetY) * 9 / 4 + offsetX * offsetX;
                if (area != 0)
                {
                    distance /= area;
                }

                int intensity = Math.Clamp(LightIntensities - distance, 0, byte.MaxValue);
                texture[row / LightStep * pitch + col / LightStep] = isPalette ? (ushort)intensity : colors[intensity];
            }
        }

        var source = Rectangle.FromLTRB(1, 1, width / 2 - 1, height / 2 - 1);
        SetRenderState(RenderState.SpecularEnable, 0);
        SetBlend(BlendFactor.DestinationColor, BlendFactor.One);
        new Texture(texture, new Size(pitch, pitch), format, screen.TexturePalette).DrawQuad(screen, destination, source, new Gamma(~color, 0xff000000), Blend, isDoubled, (position.Z + size.X + LightDepthBias) * LightDepthScale + LightDepthBase, (position.Z + size.X + LightDepthBias) * LightDepthScale + LightDepthBase);
    }

    private static int SampleLightDepth(RectangleF viewport, ReadOnlySpan<ushort> depth, PointF point, int index)
    {
        if (point.X < viewport.Left || point.X >= viewport.Right || point.Y < viewport.Top || point.Y >= viewport.Bottom)
            return LightNoDepth;

        return (depth[index] >> DepthShift) - DepthBase;
    }
}

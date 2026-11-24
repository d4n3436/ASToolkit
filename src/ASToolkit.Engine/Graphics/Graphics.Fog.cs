using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    internal const int FogDepthPerLevel = 8;
    private const uint ColorMask = 0xffffff;
    private const int FogCell = 4;
    private const int FogDepthBias = 0x400;
    private const int FogEdgeReach = 10;
    private const int FogDepthSpan = 3;
    private const int FogCellMask = 3;
    private const int FogBufferSize = 256;
    private const int FogDepthOffset = 0x3fe;
    private const float FogDepthScale = 1.5258789e-05f;

    [EngineRoutine(0x00431e40)]
    internal void DrawFog(Screen screen, Vector2 topLeft, Vector2 bottomRight, int near, int far, uint color, ReadOnlySpan<ushort> table, int level, bool isLight)
    {
        var viewport = Viewport;
        int left = (int)topLeft.X;
        int top = (int)topLeft.Y;
        int right = (int)bottomRight.X;
        int bottom = (int)bottomRight.Y;
        if (Flags.HasFlag(GraphicsFlags.Software) || table.IsEmpty || viewport.Left > bottomRight.X || !(topLeft.X < viewport.Right) || viewport.Top > bottomRight.Y || !(topLeft.Y < viewport.Bottom))
            return;

        if (topLeft.X < viewport.Left)
        {
            left = (int)viewport.Left;
        }

        if (topLeft.Y < viewport.Top)
        {
            top = (int)viewport.Top;
        }

        if (viewport.Right <= bottomRight.X)
        {
            right = (int)viewport.Right;
        }

        if (viewport.Bottom <= bottomRight.Y)
        {
            bottom = (int)viewport.Bottom;
        }

        int width = right - left;
        int height = bottom - top;
        if (width <= FogCellMask || height <= FogCellMask)
            return;

        int nearLevel = level + (near - far) * FogDepthPerLevel;
        int columns = width + FogCellMask >> 2;
        int rows = height + FogCellMask >> 2;
        var buffer = screen.TextureBuffer;
        var format = screen.TextureFormat;
        bool isPalette = format == VidPixelFormat.P8;
        var depth = screen.Depth;
        ushort last = 0;

        for (int row = 0; row < rows; row++)
        {
            int index = (top + row * FogCell) * screen.DepthWidth + left;

            for (int column = 0; column < columns; column++)
            {
                int distance = Math.Min(depth[index], depth[index + FogDepthSpan]) - FogDepthBias;

                if (level < distance)
                {
                    if (level + FogEdgeReach >= distance)
                    {
                        last = 0;
                    }
                }
                else if (nearLevel < distance)
                {
                    last = isPalette ? (byte)table[level - distance] : table[level - distance];
                }
                else
                {
                    last = isPalette ? byte.MaxValue : table[level - nearLevel];
                }

                buffer[row * FogBufferSize + column] = last;
                index += FogCell;
            }
        }

        SetAlphaBlend(isLight ? BlendFactor.Zero : BlendFactor.One, BlendFactor.InverseSourceColor);
        new Texture(buffer, new Size(FogBufferSize, FogBufferSize), format, screen.TexturePalette).DrawQuad(screen, new Rectangle(left, top, width, height), new Rectangle(0, 0, columns, rows), new Gamma(~color, Gamma.OpaqueAlpha), Blend, topDepth: (level + FogDepthOffset) * FogDepthScale, bottomDepth: (level + FogDepthOffset) * FogDepthScale);
    }
}

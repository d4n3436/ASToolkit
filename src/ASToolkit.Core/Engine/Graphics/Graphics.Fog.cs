using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

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

    [EngineRoutine(0x00431e40)]
    internal static void DrawFog(Screen screen, Vector2 topLeft, Vector2 bottomRight, int near, int far, uint color, ReadOnlySpan<ushort> table, int level, bool isLight)
    {
        var clip = screen.Clip;
        if (table.IsEmpty || topLeft.X >= clip.Right || bottomRight.X < clip.Left || topLeft.Y >= clip.Bottom || bottomRight.Y < clip.Top)
            return;

        int left = topLeft.X < clip.Left ? clip.Left : (int)topLeft.X;
        int top = topLeft.Y < clip.Top ? clip.Top : (int)topLeft.Y;
        int right = clip.Right <= bottomRight.X ? clip.Right : (int)bottomRight.X;
        int width = right - left;
        int height = (clip.Bottom <= bottomRight.Y ? clip.Bottom : (int)bottomRight.Y) - top;

        if (width <= FogCellMask || height <= FogCellMask)
            return;

        int nearLevel = level + (near - far) * FogDepthPerLevel;
        int columns = Math.Min(width + FogCellMask >> 2, FogBufferSize);
        int rows = Math.Min(height + FogCellMask >> 2, FogBufferSize);
        ushort[] buffer = new ushort[FogBufferSize * FogBufferSize];
        var depth = screen.Depth;
        ushort last = 0;

        for (int row = 0; row < rows; row++)
        {
            int index = (top + row * FogCell) * screen.DepthWidth + left;

            for (int column = 0; column < columns; column++)
            {
                int distance = Math.Min(GetDepth(depth, index), GetDepth(depth, index + FogDepthSpan)) - FogDepthBias;

                if (level < distance)
                {
                    if (level + FogEdgeReach >= distance)
                    {
                        last = 0;
                    }
                }
                else if (nearLevel < distance)
                {
                    last = table[level - distance];
                }
                else
                {
                    last = table[level - nearLevel];
                }

                buffer[row * FogBufferSize + column] = last;
                index += FogCell;
            }
        }

        new Texture(buffer, new Size(FogBufferSize, FogBufferSize), VidPixelFormat.R5G6B5).DrawQuad(screen, new Rectangle(left, top, width, height), new Rectangle(0, 0, columns, rows), new Gamma(color & ColorMask, 0), new QuadBlend(isLight ? BlendFactor.One : BlendFactor.Zero, BlendFactor.InverseSourceColor));
    }

    /// <summary>
    /// Reads the depth of a pixel from a depth buffer.
    /// </summary>
    /// <param name="depth">The depth buffer.</param>
    /// <param name="index">The index of the pixel.</param>
    /// <returns>The depth of the pixel, or 0 outside the buffer.</returns>
    public static int GetDepth(Span<ushort> depth, int index) => (uint)index < (uint)depth.Length ? depth[index] : 0;
}

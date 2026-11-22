using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents one frame of a hardware layer definition, a texture with the triangle mesh that draws it.
/// </summary>
/// <param name="Size">The size of the texture, in texels.</param>
/// <param name="Format">The layout of the texels, R5G6B5 or A4R4G4B4.</param>
/// <param name="Texels">The texels row by row.</param>
/// <param name="Vertices">The vertices of the mesh.</param>
/// <param name="Indexes">The vertex indexes of the mesh, three for each triangle.</param>
public readonly record struct HardwareLayerFrame(Size Size, VidPixelFormat Format, ushort[] Texels, HardwareLayerVertex[] Vertices, ushort[] Indexes)
{
    private const int AlphaReference = 0x20;
    private const int StripVertexCount = 4;

    /// <summary>
    /// Draws the mesh of the frame with its texture, without culling and with an alpha test.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="transform">The world, view and projection transforms of the device multiplied in this order.</param>
    /// <param name="blend">How the mesh combines with the colors underneath.</param>
    /// <param name="factor">The texture factor, a color multiplied with the texels.</param>
    [EngineRoutine(0x0041f120)]
    public void Draw(Screen screen, in Matrix4x4 transform, QuadBlend blend, uint factor)
    {
        var texture = new Texture(Texels, Size, Format);
        if (Indexes.Length == 0)
        {
            texture.DrawTriangles(screen, Vertices.AsSpan(0, Math.Min(Vertices.Length, StripVertexCount)), [0, 1, 2, 1, 2, 3], transform, blend, factor, AlphaReference);
            return;
        }

        texture.DrawTriangles(screen, Vertices, Indexes, transform, blend, factor, AlphaReference);
    }
}

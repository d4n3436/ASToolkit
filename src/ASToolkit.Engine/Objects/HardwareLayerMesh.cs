using System.Numerics;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the triangle mesh of one frame of a hardware layer object ID.
/// </summary>
/// <param name="Vertices">The vertices of the mesh.</param>
/// <param name="Indexes">The vertex indexes of the mesh, three for each triangle.</param>
public readonly record struct HardwareLayerMesh(HardwareLayerVertex[] Vertices, ushort[] Indexes)
{
    private const int AlphaReference = 0x20;
    private const int StripVertexCount = 4;

    /// <summary>
    /// Draws the mesh with a texture, without culling and with an alpha test.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="texture">The texture of the frame.</param>
    /// <param name="transform">The world, view and projection transforms of the device multiplied in this order.</param>
    /// <param name="blend">How the mesh combines with the colors underneath.</param>
    /// <param name="factor">The texture factor, a color multiplied with the texels.</param>
    [EngineRoutine(0x0041f120)]
    public void Draw(Screen screen, Direct3DTexture texture, in Matrix4x4 transform, QuadBlend blend, uint factor)
    {
        if (Indexes.Length == 0)
        {
            texture.DrawTriangles(screen, Vertices.AsSpan(0, Math.Min(Vertices.Length, StripVertexCount)), [0, 1, 2, 1, 2, 3], transform, blend, factor, AlphaReference);
            return;
        }

        texture.DrawTriangles(screen, Vertices, Indexes, transform, blend, factor, AlphaReference);
    }
}

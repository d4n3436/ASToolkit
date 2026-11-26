namespace ASToolkit.Engine;

/// <summary>
/// Represents the triangle mesh of one frame of a hardware layer object ID.
/// </summary>
/// <param name="Vertices">The vertices of the mesh.</param>
/// <param name="Indexes">The vertex indexes of the mesh, three for each triangle.</param>
/// <param name="Texture">The texture of the frame.</param>
public readonly record struct HardwareLayerMesh(HardwareLayerVertex[] Vertices, ushort[] Indexes, Texture Texture)
{
    private const int AlphaReference = 0x20;
    private const int StripVertexCount = 4;

    /// <summary>
    /// Draws the mesh with its texture, without culling and with an alpha test, using the transforms, blend and texture factor of the graphics.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    [EngineRoutine(0x0041f120)]
    public void Draw(Screen screen)
    {
        var graphics = Texture.Graphics;
        var transform = graphics.WorldTransform * graphics.ViewTransform * graphics.Projection;
        if (Indexes.Length == 0)
        {
            Texture.View.DrawTriangles(screen, Vertices.AsSpan(0, Math.Min(Vertices.Length, StripVertexCount)), [0, 1, 2, 1, 2, 3], transform, graphics.Blend, graphics.TextureFactor, AlphaReference);
            return;
        }

        Texture.View.DrawTriangles(screen, Vertices, Indexes, transform, graphics.Blend, graphics.TextureFactor, AlphaReference);
    }
}

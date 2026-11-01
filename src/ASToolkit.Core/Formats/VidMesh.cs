using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of the triangles that give the pixels of a cached sprite frame their height.
/// </summary>
public readonly struct VidMesh
{
    /// <summary>
    /// Represents how far outside a triangle a pixel center may lie and still take its height.
    /// </summary>
    public const float Tolerance = 0.01f;

    private const int IndexSize = sizeof(ushort);
    private const int VertexSize = sizeof(uint);
    private const int TriangleSize = 3 * IndexSize;

    private readonly ReadOnlyMemory<byte> _indices;
    private readonly ReadOnlyMemory<byte> _vertices;

    internal VidMesh(ReadOnlyMemory<byte> indices, ReadOnlyMemory<byte> vertices)
    {
        _indices = indices;
        _vertices = vertices;
    }

    /// <summary>
    /// Gets the number of triangles.
    /// </summary>
    public int TriangleCount => _indices.Length / TriangleSize;

    /// <summary>
    /// Gets the number of vertices.
    /// </summary>
    public int VertexCount => _vertices.Length / VertexSize;

    /// <summary>
    /// Returns a vertex.
    /// </summary>
    public VidVertex GetVertex(int index) => new(MemoryMarshal.Read<uint>(_vertices.Span[(index * VertexSize)..]));

    /// <summary>
    /// Returns the vertices of a triangle.
    /// </summary>
    public (VidVertex A, VidVertex B, VidVertex C) GetTriangle(int index)
    {
        var span = _indices.Span[(index * TriangleSize)..];
        return (GetVertex(MemoryMarshal.Read<ushort>(span)), GetVertex(MemoryMarshal.Read<ushort>(span[IndexSize..])), GetVertex(MemoryMarshal.Read<ushort>(span[(2 * IndexSize)..])));
    }

    /// <summary>
    /// Returns the highest triangle height at each pixel of a frame, or NaN where no triangle covers it.
    /// </summary>
    public float[] RasterizeHeights(int width, int height)
    {
        float[] heights = new float[width * height];
        Array.Fill(heights, float.NaN);
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            var (a, b, c) = GetTriangle(triangle);
            float area = (b.Y - c.Y) * (float)(a.X - c.X) + (c.X - b.X) * (float)(a.Y - c.Y);
            if (Math.Abs(area) < 1e-9f)
            {
                continue;
            }

            int minX = Math.Max(0, Math.Min(a.X, Math.Min(b.X, c.X)));
            int maxX = Math.Min(width - 1, Math.Max(a.X, Math.Max(b.X, c.X)));
            int minY = Math.Max(0, Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            int maxY = Math.Min(height - 1, Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float weightA = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / area;
                    float weightB = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / area;
                    float weightC = 1 - weightA - weightB;
                    if (weightA < -Tolerance || weightB < -Tolerance || weightC < -Tolerance)
                    {
                        continue;
                    }

                    float z = weightA * a.Z + weightB * b.Z + weightC * c.Z;
                    ref float cell = ref heights[y * width + x];
                    if (float.IsNaN(cell) || z > cell)
                    {
                        cell = z;
                    }
                }
            }
        }

        return heights;
    }
}

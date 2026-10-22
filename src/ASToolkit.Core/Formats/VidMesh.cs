using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of the triangles that give the pixels of a cached sprite frame their height.
/// </summary>
public readonly struct VidMesh
{
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
}

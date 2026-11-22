using System.Drawing;

namespace ASToolkit.Formats;

/// <summary>
/// Represents a read-only view of the pixels of one sprite frame, enumerated as runs from the top row down.
/// </summary>
public readonly struct VidFrame
{
    internal ReadOnlyMemory<byte> Stream { get; }

    internal bool HasDepth { get; }

    internal bool IsPaletted { get; }

    internal int Stride { get; }

    internal int CanvasWidth { get; }

    internal VidFrame(ReadOnlyMemory<byte> rows, int top, int rowCount, bool hasDepth, bool isPaletted)
    {
        Stream = rows;
        Area = new Rectangle(0, top, 0, rowCount);
        HasDepth = hasDepth;
        IsPaletted = isPaletted;
        PixelSize = isPaletted ? 1 : 2;
        Stride = PixelSize;
    }

    internal VidFrame(ReadOnlyMemory<byte> stream, Rectangle area, int canvasWidth, bool hasDepth, bool isPaletted, int colorSize, int stride, VidMesh mesh)
    {
        Stream = stream;
        Mesh = mesh;
        Area = area;
        CanvasWidth = canvasWidth;
        HasDepth = hasDepth;
        IsPaletted = isPaletted;
        PixelSize = colorSize;
        Stride = stride;
    }

    /// <summary>
    /// Gets the area of the frame in canvas coordinates: its first row and number of rows, and for a CADR frame its first column and number of columns (zero for frames stored as rows).
    /// </summary>
    public Rectangle Area { get; }

    /// <summary>
    /// Gets the triangles that give the pixels a height; empty for frames that don't carry any.
    /// </summary>
    public VidMesh Mesh { get; }

    /// <summary>
    /// Gets the size in bytes of one color: one for a palette index, two for a 16-bit color, four for a 32-bit color.
    /// </summary>
    public int PixelSize { get; }

    /// <summary>
    /// Returns an enumerator over the runs of the frame.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public VidFrameEnumerator GetEnumerator() => new(this);
}

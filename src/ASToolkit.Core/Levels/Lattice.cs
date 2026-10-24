using System.Drawing;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents an isometric lattice. Vertex (a, b) lies at origin + a * (StepX, -StepY) + b * (StepX, StepY).
/// </summary>
public sealed class Lattice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Lattice"/> class.
    /// </summary>
    public Lattice(float stepX, float stepY, float originX = 0, float originY = 0)
    {
        StepX = stepX;
        StepY = stepY;
        OriginX = originX;
        OriginY = originY;
    }

    /// <summary>
    /// Gets the horizontal step.
    /// </summary>
    public float StepX { get; }

    /// <summary>
    /// Gets the vertical step.
    /// </summary>
    public float StepY { get; }

    /// <summary>
    /// Gets or sets the x of vertex (0, 0).
    /// </summary>
    public float OriginX { get; set; }

    /// <summary>
    /// Gets or sets the y of vertex (0, 0).
    /// </summary>
    public float OriginY { get; set; }

    /// <summary>
    /// Gets the length of one cell step in map pixels.
    /// </summary>
    public float CellStep => MathF.Sqrt(StepX * StepX + StepY * StepY);

    /// <summary>
    /// Returns the map position of lattice point (a, b).
    /// </summary>
    public PointF ToWorld(float a, float b) => new(OriginX + StepX * (a + b), OriginY + StepY * (b - a));

    /// <summary>
    /// Returns the lattice coordinates of a map position.
    /// </summary>
    public (float U, float V) ToLattice(float x, float y)
    {
        float p = (x - OriginX) / StepX;
        float q = (y - OriginY) / StepY;
        return ((p - q) / 2, (p + q) / 2);
    }

    /// <summary>
    /// Returns the cell that holds a map position.
    /// </summary>
    public Cell GetCell(float x, float y)
    {
        (float u, float v) = ToLattice(x, y);
        return new Cell((int)MathF.Floor(u), (int)MathF.Floor(v));
    }

    /// <summary>
    /// Returns the map position of a cell's center.
    /// </summary>
    public PointF GetCenter(Cell c) => ToWorld(c.U + 0.5f, c.V + 0.5f);

    /// <summary>
    /// Returns a lattice with half the step and the same origin.
    /// </summary>
    public Lattice Halve() => new(StepX / 2, StepY / 2, OriginX, OriginY);

    /// <summary>
    /// Returns whether a value is within 0.06 of an integer.
    /// </summary>
    public static bool IsNearInteger(float t) => Math.Abs(t - MathF.Round(t)) < 0.06f;
}

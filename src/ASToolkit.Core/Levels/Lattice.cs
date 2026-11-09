using System.Drawing;
using System.Numerics;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents an isometric lattice. Vertex (a, b) lies at origin + a * (StepX, -StepY) + b * (StepX, StepY).
/// </summary>
public sealed class Lattice
{
    /// <summary>
    /// Represents the basement lattice step.
    /// </summary>
    public static readonly PointF BasementStep = new(40, 28);

    /// <summary>
    /// Represents the concrete fine lattice step.
    /// </summary>
    public static readonly PointF ConcreteStep = new(45, 32);

    /// <summary>
    /// Initializes a new instance of the <see cref="Lattice"/> class.
    /// </summary>
    public Lattice(float stepX, float stepY, float originX = 0, float originY = 0)
    {
        StepX = stepX;
        StepY = stepY;
        OriginX = originX;
        OriginY = originY;
        CellStep = MathF.Sqrt(stepX * stepX + stepY * stepY);
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
    public float CellStep { get; }

    /// <summary>
    /// Returns the map position of lattice point (a, b).
    /// </summary>
    public PointF ToWorld(float a, float b) => new(OriginX + StepX * (a + b), OriginY + StepY * (b - a));

    /// <summary>
    /// Returns the lattice coordinates of a map position.
    /// </summary>
    public GridPoint ToLattice(float x, float y)
    {
        float p = (x - OriginX) / StepX;
        float q = (y - OriginY) / StepY;
        return new GridPoint((p - q) / 2, (p + q) / 2);
    }

    /// <summary>
    /// Returns the lattice coordinates of a map point.
    /// </summary>
    public GridPoint ToLattice(PointF point) => ToLattice(point.X, point.Y);

    /// <summary>
    /// Returns the lattice coordinates of a map position, leaving out its height.
    /// </summary>
    public GridPoint ToLattice(Vector3 position) => ToLattice(position.X, position.Y);

    /// <summary>
    /// Returns the cell that holds a map position.
    /// </summary>
    public Cell GetCell(float x, float y)
    {
        (float u, float v) = ToLattice(x, y);
        return new Cell((int)MathF.Floor(u), (int)MathF.Floor(v));
    }

    /// <summary>
    /// Returns the cell that holds a map point.
    /// </summary>
    public Cell GetCell(PointF point) => GetCell(point.X, point.Y);

    /// <summary>
    /// Returns the cell that holds a map position, leaving out its height.
    /// </summary>
    public Cell GetCell(Vector3 position) => GetCell(position.X, position.Y);

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

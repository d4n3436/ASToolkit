namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents an isometric lattice. Vertex (a, b) lies at origin + a * (StepX, -StepY) + b * (StepX, StepY).
/// </summary>
public sealed class Lattice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Lattice"/> class.
    /// </summary>
    public Lattice(double stepX, double stepY, double originX = 0, double originY = 0)
    {
        StepX = stepX;
        StepY = stepY;
        OriginX = originX;
        OriginY = originY;
    }

    /// <summary>
    /// Gets the horizontal step.
    /// </summary>
    public double StepX { get; }

    /// <summary>
    /// Gets the vertical step.
    /// </summary>
    public double StepY { get; }

    /// <summary>
    /// Gets or sets the x of vertex (0, 0).
    /// </summary>
    public double OriginX { get; set; }

    /// <summary>
    /// Gets or sets the y of vertex (0, 0).
    /// </summary>
    public double OriginY { get; set; }

    /// <summary>
    /// Gets the length of one cell step in map pixels.
    /// </summary>
    public double CellStep => Math.Sqrt(StepX * StepX + StepY * StepY);

    /// <summary>
    /// Returns the map position of lattice point (a, b).
    /// </summary>
    public (double X, double Y) ToWorld(double a, double b) => (OriginX + StepX * (a + b), OriginY + StepY * (b - a));

    /// <summary>
    /// Returns the lattice coordinates of a map position.
    /// </summary>
    public (double U, double V) ToLattice(double x, double y)
    {
        double p = (x - OriginX) / StepX;
        double q = (y - OriginY) / StepY;
        return ((p - q) / 2, (p + q) / 2);
    }

    /// <summary>
    /// Returns the cell that holds a map position.
    /// </summary>
    public Cell GetCell(double x, double y)
    {
        (double u, double v) = ToLattice(x, y);
        return new Cell((int)Math.Floor(u), (int)Math.Floor(v));
    }

    /// <summary>
    /// Returns the map position of a cell's center.
    /// </summary>
    public (double X, double Y) GetCenter(Cell c) => ToWorld(c.U + 0.5, c.V + 0.5);

    /// <summary>
    /// Returns a lattice with half the step and the same origin.
    /// </summary>
    public Lattice Halve() => new(StepX / 2, StepY / 2, OriginX, OriginY);

    /// <summary>
    /// Returns whether a value is within 0.06 of an integer.
    /// </summary>
    public static bool IsNearInteger(double t) => Math.Abs(t - Math.Round(t)) < 0.06;
}

using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents a rectangle of units on the layout grid.
/// </summary>
/// <param name="U">The first unit along u.</param>
/// <param name="V">The first unit along v.</param>
/// <param name="W">The width along u.</param>
/// <param name="H">The height along v.</param>
public readonly record struct UnitRect(int U, int V, int W, int H)
{
    /// <summary>
    /// Gets the number of units.
    /// </summary>
    public int Area => W * H;

    /// <summary>
    /// Gets the units inside the rectangle.
    /// </summary>
    public IEnumerable<Cell> Units
    {
        get
        {
            for (int u = U; u < U + W; u++)
            {
                for (int v = V; v < V + H; v++)
                {
                    yield return new Cell(u, v);
                }
            }
        }
    }

    /// <summary>
    /// Returns whether a unit lies inside the rectangle.
    /// </summary>
    public bool Contains(Cell c) => c.U >= U && c.U < U + W && c.V >= V && c.V < V + H;

    /// <summary>
    /// Returns the unit edges along the common boundary with an adjacent rectangle, empty when the two do not touch.
    /// </summary>
    public EdgeKey[] GetSharedEdges(UnitRect other)
    {
        var edges = new List<EdgeKey>();
        if (other.U == U + W || U == other.U + other.W)
        {
            int e = other.U == U + W ? other.U : U;
            for (int v = Math.Max(V, other.V); v < Math.Min(V + H, other.V + other.H); v++)
            {
                edges.Add(new EdgeKey('u', e, v));
            }
        }
        else if (other.V == V + H || V == other.V + other.H)
        {
            int e = other.V == V + H ? other.V : V;
            for (int u = Math.Max(U, other.U); u < Math.Min(U + W, other.U + other.W); u++)
            {
                edges.Add(new EdgeKey('v', u, e));
            }
        }

        return edges.ToArray();
    }
}

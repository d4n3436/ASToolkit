using System.Numerics;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents the bounds of a level's walkable cells.
/// </summary>
/// <param name="Minimum">The top-left corner.</param>
/// <param name="Maximum">The bottom-right corner.</param>
public readonly record struct WorldBounds(Vector2 Minimum, Vector2 Maximum)
{
    private const float Margin = 512;

    /// <summary>
    /// Measures the bounds of a level.
    /// </summary>
    /// <param name="model">The level.</param>
    /// <returns>The bounds.</returns>
    public static WorldBounds Measure(LevelModel model) => Measure(model.Cells, model.Lattice);

    /// <summary>
    /// Measures the bounds of cells.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="lattice">The lattice of the cells.</param>
    /// <returns>The bounds.</returns>
    public static WorldBounds Measure(IEnumerable<Cell> cells, Lattice lattice)
    {
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        bool any = false;
        foreach (var cell in cells)
        {
            any = true;
            var center = lattice.GetCenter(cell).ToVector2();
            minimum = Vector2.Min(minimum, center);
            maximum = Vector2.Max(maximum, center);
        }

        if (!any)
        {
            minimum = maximum = Vector2.Zero;
        }

        return new WorldBounds(minimum - new Vector2(Margin), maximum + new Vector2(Margin));
    }
}

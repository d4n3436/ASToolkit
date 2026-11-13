using System.Numerics;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

internal readonly record struct WorldBounds(Vector2 Minimum, Vector2 Maximum)
{
    private const float Margin = 512;

    public static WorldBounds Measure(LevelModel model) => Measure(model.Cells, model.Lattice);

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

using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

/// <summary>
/// Provides placement of rectangles beside each other and strips inside them on a building grid.
/// </summary>
internal static class UnitRectExtensions
{
    extension(Side side)
    {
        public bool RunsAlongU => side is Side.North or Side.South;
    }

    extension(UnitRect rect)
    {
        // Returns a rect touching this one on the side, along long parallel to that side from at, depth deep
        public UnitRect Beside(Side side, int along, int depth, int at) => side switch
        {
            Side.North => new UnitRect(at, rect.V - depth, along, depth),
            Side.South => new UnitRect(at, rect.V + rect.H, along, depth),
            Side.West => new UnitRect(rect.U - depth, at, depth, along),
            _ => new UnitRect(rect.U + rect.W, at, depth, along)
        };

        // Returns a strip inside this rect along the side, along long parallel to that side from at, depth deep
        public UnitRect Inside(Side side, int along, int depth, int at) => side switch
        {
            Side.North => new UnitRect(at, rect.V, along, depth),
            Side.South => new UnitRect(at, rect.V + rect.H - depth, along, depth),
            Side.West => new UnitRect(rect.U, at, depth, along),
            _ => new UnitRect(rect.U + rect.W - depth, at, depth, along)
        };

        public (int Low, int High) GetSpan(Side side) => side.RunsAlongU ? (rect.U, rect.U + rect.W) : (rect.V, rect.V + rect.H);

        public bool SharesWall(UnitRect other) =>
            ((rect.U == other.U + other.W || other.U == rect.U + rect.W) && rect.V < other.V + other.H && other.V < rect.V + rect.H)
            || ((rect.V == other.V + other.H || other.V == rect.V + rect.H) && rect.U < other.U + other.W && other.U < rect.U + rect.W);
    }
}

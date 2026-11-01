using System.Drawing;
using ASToolkit.Core.Generation.Layout;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Content;

internal sealed class HallAxis
{
    private readonly PlacementContext _ctx;
    private readonly IReadOnlyList<Cell> _cells;
    private readonly bool _alongU;
    private readonly float _middleU;
    private readonly float _middleV;
    private readonly float _half;
    private readonly int _entrySign;

    private HallAxis(PlacementContext ctx, IReadOnlyList<Cell> cells)
    {
        _ctx = ctx;
        _cells = cells;
        int minU = cells.Min(c => c.U), maxU = cells.Max(c => c.U) + 1;
        int minV = cells.Min(c => c.V), maxV = cells.Max(c => c.V) + 1;
        _alongU = maxU - minU >= maxV - minV;
        _middleU = (minU + maxU) / 2f;
        _middleV = (minV + maxV) / 2f;
        _half = Math.Max(1, (_alongU ? maxU - minU : maxV - minV) / 2f);
        
        var entry = cells
            .Where(ctx.Distance.ContainsKey)
            .DefaultIfEmpty(cells[0])
            .MinBy(c => ctx.Distance.GetValueOrDefault(c));
        
        _entrySign = GetPosition(entry) >= 0 ? 1 : -1;
    }

    public static HallAxis Measure(PlacementContext ctx, PlanRegion region) => new(ctx, ctx.GetRegionCells(region));

    // Returns 1 at the end the walk enters from and -1 at the far end
    public float GetEntryShare(Cell cell) => _entrySign * GetPosition(cell);

    // Returns the center of the cell nearest a point on the long axis, the given share of the way to the far end
    public PointF GetFarPoint(float share)
    {
        float along = -_entrySign * share * _half;
        (float u, float v) = _alongU ? (_middleU + along, _middleV) : (_middleU, _middleV + along);
        var cell = _cells.MinBy(c => MathF.Abs(c.U + 0.5f - u) + MathF.Abs(c.V + 0.5f - v));
        return _ctx.Layout.Lattice.GetCenter(cell);
    }

    private float GetPosition(Cell cell) => ((_alongU ? cell.U : cell.V) + 0.5f - (_alongU ? _middleU : _middleV)) / _half;
}

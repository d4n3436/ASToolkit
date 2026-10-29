using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents a level layout on the unit grid, with a chain of stations from the start, side rooms, closets and the gate.
/// </summary>
public sealed class LayoutPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutPlan"/> class.
    /// </summary>
    public LayoutPlan(ThemeSettings theme, LayoutStyleProfile profile)
    {
        Theme = theme;
        Profile = profile;
    }

    /// <summary>
    /// Gets the theme settings.
    /// </summary>
    public ThemeSettings Theme { get; }

    /// <summary>
    /// Gets the room sizing and connection weights the layout is built with.
    /// </summary>
    public LayoutStyleProfile Profile { get; }

    /// <summary>
    /// Gets or sets the largest size of a room, in units.
    /// </summary>
    public int RoomCap { get; set; }

    /// <summary>
    /// Gets the regions; a region's ID is its index.
    /// </summary>
    public List<PlanRegion> Regions { get; } = [];

    /// <summary>
    /// Gets the links between regions.
    /// </summary>
    public List<PlanLink> Links { get; } = [];

    /// <summary>
    /// Gets the region of every occupied unit.
    /// </summary>
    public Dictionary<Cell, int> Occupancy { get; } = [];

    /// <summary>
    /// Gets the stations in route order.
    /// </summary>
    public List<PlanRegion> Stations { get; } = [];

    /// <summary>
    /// Gets or sets the gate kind, or null for a level without a gate.
    /// </summary>
    public GateKind? Gate { get; set; }

    /// <summary>
    /// Gets or sets the link that holds the gate.
    /// </summary>
    public PlanLink? GateLink { get; set; }

    /// <summary>
    /// Gets or sets the room of the gate's key, the breaker or generator.
    /// </summary>
    public PlanRegion? KeyRoom { get; set; }

    /// <summary>
    /// Gets or sets the link the blackout trigger sits in.
    /// </summary>
    public PlanLink? BlackoutLink { get; set; }

    /// <summary>
    /// Gets the regions of each dead-end branch off the route.
    /// </summary>
    public List<List<PlanRegion>> Branches { get; } = [];

    /// <summary>
    /// Gets the number of occupied units.
    /// </summary>
    public int Area => Occupancy.Count;

    /// <summary>
    /// Gets or sets the box every region must stay inside, or null for no limit.
    /// </summary>
    public UnitRect? Bounds { get; set; }

    /// <summary>
    /// Returns whether a rectangle lies inside the bounds.
    /// </summary>
    public bool IsInBounds(UnitRect r) =>
        Bounds is null || (r.U >= Bounds.Value.U && r.V >= Bounds.Value.V && r.U + r.W <= Bounds.Value.U + Bounds.Value.W && r.V + r.H <= Bounds.Value.V + Bounds.Value.H);

    /// <summary>
    /// Returns whether a rectangle, grown by a margin, is free and inside the bounds.
    /// </summary>
    public bool IsFree(UnitRect r, int margin = 0)
    {
        if (!IsInBounds(r))
        {
            return false;
        }

        for (int u = r.U - margin; u < r.U + r.W + margin; u++)
        {
            for (int v = r.V - margin; v < r.V + r.H + margin; v++)
            {
                if (Occupancy.ContainsKey(new Cell(u, v)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether a unit diagonal to a corner of a rectangle belongs to a region other than the given one.
    /// </summary>
    public bool TouchesOtherAtCorner(UnitRect r, int exceptId)
    {
        foreach (int u in (int[])[r.U - 1, r.U + r.W])
        {
            foreach (int v in (int[])[r.V - 1, r.V + r.H])
            {
                if (Occupancy.TryGetValue(new Cell(u, v), out int id) && id != exceptId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a region covering a rectangle.
    /// </summary>
    public PlanRegion AddRegion(RegionKind kind, UnitRect rect, int station, int? host, bool isHall = false)
    {
        var region = new PlanRegion { Id = Regions.Count, Kind = kind, Station = station, Host = host, IsHall = isHall };
        Regions.Add(region);
        region.Rects.Add(rect);
        foreach (var unit in rect.Units)
        {
            Occupancy[unit] = region.Id;
            region.Units.Add(unit);
        }

        return region;
    }

    /// <summary>
    /// Returns the links of a region.
    /// </summary>
    public IEnumerable<PlanLink> GetLinks(PlanRegion region) => Links.Where(l => l.From == region.Id || l.To == region.Id);
}

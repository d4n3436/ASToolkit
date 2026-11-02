namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents the layouts, redraws, necks, cuts, extra entrances, loops, rings, connectors and corridors an area builder draws and builds.
/// </summary>
public sealed class AreaBuildCounts
{
    /// <summary>
    /// Gets the number of layouts built.
    /// </summary>
    public int Layouts { get; internal set; }

    /// <summary>
    /// Gets the number of layouts redrawn because a composite found no spot.
    /// </summary>
    public int LayoutRedraws { get; internal set; }

    /// <summary>
    /// Gets the number of builds that left a room out after every redraw.
    /// </summary>
    public int FailedBuilds { get; internal set; }

    /// <summary>
    /// Gets the number of composites drawn, redraws included.
    /// </summary>
    public int Composites { get; internal set; }

    /// <summary>
    /// Gets the number of composites redrawn because they found no spot.
    /// </summary>
    public int CompositeRedraws { get; internal set; }

    /// <summary>
    /// Gets the number of path rooms drawn bare with their blocks attached one by one.
    /// </summary>
    public int BareComposites { get; internal set; }

    /// <summary>
    /// Gets the number of necks drawn.
    /// </summary>
    public int NecksDrawn { get; internal set; }

    /// <summary>
    /// Gets the number of necks built.
    /// </summary>
    public int NecksBuilt { get; internal set; }

    /// <summary>
    /// Gets the number of path-room cuts drawn with their composite.
    /// </summary>
    public int CutsPlanned { get; internal set; }

    /// <summary>
    /// Gets the number of path-room cuts dropped because no cut class leaves the room a door's width of wall.
    /// </summary>
    public int CutsDropped { get; internal set; }

    /// <summary>
    /// Gets the number of outline cuts tried on placed rooms.
    /// </summary>
    public int CutsDrawn { get; internal set; }

    /// <summary>
    /// Gets the number of outline cuts built.
    /// </summary>
    public int CutsBuilt { get; internal set; }

    /// <summary>
    /// Gets the number of connector rooms drawn.
    /// </summary>
    public int Connectors { get; internal set; }

    /// <summary>
    /// Gets the number of corridors laid.
    /// </summary>
    public int Corridors { get; internal set; }

    /// <summary>
    /// Gets the number of branch chains turned onto their parent's side.
    /// </summary>
    public int BranchTurns { get; internal set; }

    /// <summary>
    /// Gets the number of extra entrances the missions drew.
    /// </summary>
    public int EntrancesDrawn { get; internal set; }

    /// <summary>
    /// Gets the number of extra entrances built.
    /// </summary>
    public int EntrancesBuilt { get; internal set; }

    /// <summary>
    /// Gets the number of extra entrances built between other rooms of their phases than the drawn ones.
    /// </summary>
    public int EntrancesMoved { get; internal set; }

    /// <summary>
    /// Gets the number of loops the missions drew.
    /// </summary>
    public int LoopsDrawn { get; internal set; }

    /// <summary>
    /// Gets the number of loops built.
    /// </summary>
    public int LoopsBuilt { get; internal set; }

    /// <summary>
    /// Gets the number of loops built between other rooms of their phase than the drawn ones.
    /// </summary>
    public int LoopsMoved { get; internal set; }

    /// <summary>
    /// Gets the number of branch chains laid out as rings around a court.
    /// </summary>
    public int Rings { get; internal set; }

    /// <summary>
    /// Adds the counts of another set to these.
    /// </summary>
    public void Add(AreaBuildCounts other)
    {
        Layouts += other.Layouts;
        LayoutRedraws += other.LayoutRedraws;
        FailedBuilds += other.FailedBuilds;
        Composites += other.Composites;
        CompositeRedraws += other.CompositeRedraws;
        BareComposites += other.BareComposites;
        NecksDrawn += other.NecksDrawn;
        NecksBuilt += other.NecksBuilt;
        CutsPlanned += other.CutsPlanned;
        CutsDropped += other.CutsDropped;
        CutsDrawn += other.CutsDrawn;
        CutsBuilt += other.CutsBuilt;
        Connectors += other.Connectors;
        Corridors += other.Corridors;
        BranchTurns += other.BranchTurns;
        EntrancesDrawn += other.EntrancesDrawn;
        EntrancesBuilt += other.EntrancesBuilt;
        EntrancesMoved += other.EntrancesMoved;
        LoopsDrawn += other.LoopsDrawn;
        LoopsBuilt += other.LoopsBuilt;
        LoopsMoved += other.LoopsMoved;
        Rings += other.Rings;
    }
}

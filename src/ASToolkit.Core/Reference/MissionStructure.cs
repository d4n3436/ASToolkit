namespace ASToolkit.Core.Reference;

/// <summary>
/// Represents the structure a mission grammar draws for one theme.
/// </summary>
public sealed record MissionStructure
{
    /// <summary>
    /// Gets the share of a phase's rooms on its critical path.
    /// </summary>
    public required FittedValue<LogNormalFit> PathShare { get; init; }

    /// <summary>
    /// Gets the observed room counts of branch trees.
    /// </summary>
    public required FittedValue<IReadOnlyList<int>> TreeSizes { get; init; }

    /// <summary>
    /// Gets the observed shares of a branch tree's rooms its deepest room lies from the anchor.
    /// </summary>
    public required FittedValue<IReadOnlyList<double>> TreeChains { get; init; }

    /// <summary>
    /// Gets the observed positions of branch tree anchors along their phase's critical path, from 0 at its entrance to 1 at its last room.
    /// </summary>
    public required FittedValue<IReadOnlyList<double>> AnchorPositions { get; init; }

    /// <summary>
    /// Gets the share of the level's rooms a phase holds, for a first phase.
    /// </summary>
    public required FittedValue<LogNormalFit> FirstPhaseShare { get; init; }

    /// <summary>
    /// Gets the share of the level's rooms a phase holds, for a later phase.
    /// </summary>
    public required FittedValue<LogNormalFit> LaterPhaseShare { get; init; }

    /// <summary>
    /// Gets the share of the level's rooms a phase holds, for a return pass.
    /// </summary>
    public required FittedValue<LogNormalFit> OpenedPhaseShare { get; init; }

    /// <summary>
    /// Gets the share of a gated phase's rooms entered through a lock from the phase before.
    /// </summary>
    public required FittedValue<LogNormalFit> EntranceShare { get; init; }

    /// <summary>
    /// Gets the observed numbers of a gated phase's rooms entered through a lock.
    /// </summary>
    public required FittedValue<IReadOnlyList<int>> EnteredCounts { get; init; }

    /// <summary>
    /// Gets the entered rooms per room the entrances come from.
    /// </summary>
    public required FittedValue<LogNormalFit> EntrancesPerSource { get; init; }

    /// <summary>
    /// Gets the locked doors inside a phase per room of the phase.
    /// </summary>
    public required FittedValue<double> InnerLockRate { get; init; }

    /// <summary>
    /// Gets the loops per room.
    /// </summary>
    public required FittedValue<double> LoopRate { get; init; }
}

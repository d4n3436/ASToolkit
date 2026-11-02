namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Specifies the part a link plays in a mission graph.
/// </summary>
public enum MissionLinkKind
{
    /// <summary>
    /// A link of the tree that reaches every room from the start.
    /// </summary>
    Tree,

    /// <summary>
    /// A further locked door into a gated phase from a room on or next to the critical path before it.
    /// </summary>
    Entrance,

    /// <summary>
    /// A second way between two rooms of one phase.
    /// </summary>
    Loop
}

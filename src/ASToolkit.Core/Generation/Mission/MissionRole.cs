namespace ASToolkit.Core.Generation.Mission;

/// <summary>
/// Specifies the role of a room in a mission graph.
/// </summary>
public enum MissionRole
{
    /// <summary>
    /// The room the player starts in.
    /// </summary>
    Start,

    /// <summary>
    /// A room on a phase's critical path.
    /// </summary>
    Path,

    /// <summary>
    /// A room on a branch off the critical path.
    /// </summary>
    Branch,

    /// <summary>
    /// The room holding a phase's key.
    /// </summary>
    Key,

    /// <summary>
    /// A room holding a dynamite site.
    /// </summary>
    Site,

    /// <summary>
    /// A room holding dynamite charges.
    /// </summary>
    Charges,

    /// <summary>
    /// A room a return pass's key opens beside it, holding a reward on a level without dynamite sites.
    /// </summary>
    Reward,

    /// <summary>
    /// The room the level ends in.
    /// </summary>
    End
}

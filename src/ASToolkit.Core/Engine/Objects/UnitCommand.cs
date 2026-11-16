namespace ASToolkit.Core.Engine;

/// <summary>
/// Specifies the command a unit carries out.
/// </summary>
public enum UnitCommand
{
    /// <summary>
    /// No command.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// A move to the goal.
    /// </summary>
    Move = 1,
    
    /// <summary>
    /// A move to the goal in three dimensions.
    /// </summary>
    Move3D = 2,
    
    /// <summary>
    /// An attack on the goal until it dies.
    /// </summary>
    Attack = 3,
    
    /// <summary>
    /// A single attack on the goal.
    /// </summary>
    OneAttack = 4,
    
    /// <summary>
    /// An attack from range.
    /// </summary>
    DistAttack = 5,
    
    /// <summary>
    /// An attack at close range.
    /// </summary>
    NearAttack = 6,
    
    /// <summary>
    /// A patrol between points.
    /// </summary>
    Patrol = 7,
    
    /// <summary>
    /// A ram into the goal.
    /// </summary>
    Taran = 8,
    
    /// <summary>
    /// The building of a unit.
    /// </summary>
    Build = 16,
    
    /// <summary>
    /// A passive hold.
    /// </summary>
    Passive = 17,
    
    /// <summary>
    /// A pause until the timer runs out.
    /// </summary>
    Pause = 18,
    
    /// <summary>
    /// A turn in place.
    /// </summary>
    Rotate = 19,
    
    /// <summary>
    /// An engine move.
    /// </summary>
    EngineMove = 23,
    
    /// <summary>
    /// An engine mining run.
    /// </summary>
    EngineMine = 24,
    
    /// <summary>
    /// An engine patrol.
    /// </summary>
    EnginePatrol = 25,
    
    /// <summary>
    /// An engine link.
    /// </summary>
    EngineLink = 26,
    
    /// <summary>
    /// An engine clash.
    /// </summary>
    EngineClash = 27,
    
    /// <summary>
    /// An engine attack.
    /// </summary>
    EngineAttack = 28,
    
    /// <summary>
    /// A single engine attack.
    /// </summary>
    EngineOneAttack = 29,
    
    /// <summary>
    /// Any other command.
    /// </summary>
    Other = 31
}

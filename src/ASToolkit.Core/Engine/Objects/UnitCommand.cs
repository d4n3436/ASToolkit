namespace ASToolkit.Core.Engine;

internal enum UnitCommand
{
    None = 0,
    Move = 1,
    Move3D = 2,
    Attack = 3,
    OneAttack = 4,
    DistAttack = 5,
    NearAttack = 6,
    Patrol = 7,
    Taran = 8,
    Build = 16,
    Passive = 17,
    Pause = 18,
    Rotate = 19,
    EngineMove = 23,
    EngineMine = 24,
    EnginePatrol = 25,
    EngineLink = 26,
    EngineClash = 27,
    EngineAttack = 28,
    EngineOneAttack = 29,
    Other = 31
}

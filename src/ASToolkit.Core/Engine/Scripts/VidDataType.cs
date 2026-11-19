namespace ASToolkit.Core.Engine.Scripts;

/// <summary>
/// Specifies the field of a definition that the script language's GetVidData and SetVidData read and write.
/// </summary>
public enum VidDataType
{
    /// <summary>
    /// The maximum hit points of the definition.
    /// </summary>
    MaxHp = 1,

    /// <summary>
    /// The gamma of the definition, the first of four values up to 21.
    /// </summary>
    Gamma = 18,

    /// <summary>
    /// The property flags of the definition.
    /// </summary>
    Properties = 22,

    /// <summary>
    /// The range of the definition's weapon.
    /// </summary>
    BattleRange = 23,

    /// <summary>
    /// The most sprites of the definition the map may hold.
    /// </summary>
    MaxUnit = 25,

    /// <summary>
    /// The most ammunition the definition's weapon holds.
    /// </summary>
    Ammo = 26,

    /// <summary>
    /// The name of the definition.
    /// </summary>
    Name = 27,

    /// <summary>
    /// The number of sprites of the definition on the map.
    /// </summary>
    Count = 28,

    /// <summary>
    /// The number of times sprites of the definition were killed.
    /// </summary>
    KilledUnit = 29,

    /// <summary>
    /// The number of times sprites of the definition in army 0 were killed.
    /// </summary>
    KilledUnit0 = 30,

    /// <summary>
    /// The number of times sprites of the definition in army 1 were killed.
    /// </summary>
    KilledUnit1 = 31,

    /// <summary>
    /// The number of times sprites of the definition in army 2 were killed.
    /// </summary>
    KilledUnit2 = 32,

    /// <summary>
    /// The number of times sprites of the definition in army 3 were killed.
    /// </summary>
    KilledUnit3 = 33,

    /// <summary>
    /// The number of sprites of the definition in army 0 on the map.
    /// </summary>
    Count0 = 34,

    /// <summary>
    /// The number of sprites of the definition in army 1 on the map.
    /// </summary>
    Count1 = 35,

    /// <summary>
    /// The number of sprites of the definition in army 2 on the map.
    /// </summary>
    Count2 = 36,

    /// <summary>
    /// The number of sprites of the definition in army 3 on the map.
    /// </summary>
    Count3 = 37,

    /// <summary>
    /// The maximum hit points of the definition in army 0.
    /// </summary>
    MaxHp0 = 38,

    /// <summary>
    /// The maximum hit points of the definition in army 1.
    /// </summary>
    MaxHp1 = 39,

    /// <summary>
    /// The maximum hit points of the definition in army 2.
    /// </summary>
    MaxHp2 = 40,

    /// <summary>
    /// The maximum hit points of the definition in army 3.
    /// </summary>
    MaxHp3 = 41,

    /// <summary>
    /// The percentage that scales the maximum hit points of the definition in army 0.
    /// </summary>
    HpCoefficient0 = 42,

    /// <summary>
    /// The percentage that scales the maximum hit points of the definition in army 1.
    /// </summary>
    HpCoefficient1 = 43,

    /// <summary>
    /// The percentage that scales the maximum hit points of the definition in army 2.
    /// </summary>
    HpCoefficient2 = 44,

    /// <summary>
    /// The percentage that scales the maximum hit points of the definition in army 3.
    /// </summary>
    HpCoefficient3 = 45,

    /// <summary>
    /// The sprite type flags of the definition.
    /// </summary>
    SpriteType = 46,

    /// <summary>
    /// The sprite class of the definition.
    /// </summary>
    SpriteClass = 47,

    /// <summary>
    /// The speed of the definition.
    /// </summary>
    Speed = 48,

    /// <summary>
    /// The lifetime of the definition in milliseconds.
    /// </summary>
    Lifetime = 49,

    /// <summary>
    /// The range in pixels within which the definition detects an enemy.
    /// </summary>
    DetectRange = 50,

    /// <summary>
    /// The aim spread of the definition's weapon.
    /// </summary>
    WeaponAim = 51,

    /// <summary>
    /// The definition to swap records with.
    /// </summary>
    ExchangeVid = 52,

    /// <summary>
    /// The number of directions of the definition's sprite.
    /// </summary>
    Directions = 53,

    /// <summary>
    /// The collision layers the definition moves on.
    /// </summary>
    MoveMask = 54,

    /// <summary>
    /// The time the definition takes to build.
    /// </summary>
    BuildTime = 55,

    /// <summary>
    /// The flag that hides the definition from the screen.
    /// </summary>
    Hide = 56,

    /// <summary>
    /// The flag that keeps the definition from being created as a child or link.
    /// </summary>
    NotCreateAsChild = 57,

    /// <summary>
    /// The frame duration of the definition's animations.
    /// </summary>
    FrameSpeed = 58,

    /// <summary>
    /// The linked definition.
    /// </summary>
    Link = 59,

    /// <summary>
    /// The child definition of the first state, the first of 32 values up to 91.
    /// </summary>
    Child = 60,

    /// <summary>
    /// The child count of the first state, the first of 32 values up to 123.
    /// </summary>
    ChildCounts = 92,

    /// <summary>
    /// The damage the definition deals around it when it dies.
    /// </summary>
    Damage = 124,

    /// <summary>
    /// The number of times sprites of the definition were recolored.
    /// </summary>
    RecolorUnit = 125,

    /// <summary>
    /// The number of times sprites of the definition in army 0 were recolored.
    /// </summary>
    RecolorUnit0 = 126,

    /// <summary>
    /// The number of times sprites of the definition in army 1 were recolored.
    /// </summary>
    RecolorUnit1 = 127,

    /// <summary>
    /// The number of times sprites of the definition in army 2 were recolored.
    /// </summary>
    RecolorUnit2 = 128,

    /// <summary>
    /// The number of times sprites of the definition in army 3 were recolored.
    /// </summary>
    RecolorUnit3 = 129
}

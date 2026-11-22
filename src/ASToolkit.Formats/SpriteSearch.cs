namespace ASToolkit.Formats;

/// <summary>
/// Specifies which sprites a level script's sprite search accepts.
/// </summary>
[Flags]
public enum SpriteSearch
{
    /// <summary>
    /// Any sprite of any type and army.
    /// </summary>
    None = 0,

    /// <summary>
    /// The bits holding the object ID or the sprite class the search matches.
    /// </summary>
    ValueMask = (1 << 11) - 1,

    /// <summary>
    /// Sprites of the object ID in the value bits.
    /// </summary>
    Id = 1 << 11,

    /// <summary>
    /// Sprites of the sprite class in the value bits.
    /// </summary>
    Class = 1 << 12,

    /// <summary>
    /// Sprites filed in the map's grid.
    /// </summary>
    Hash = 1 << 15,

    /// <summary>
    /// Sprites of the first army.
    /// </summary>
    Army0 = 1 << 16,

    /// <summary>
    /// Sprites of the second army.
    /// </summary>
    Army1 = 1 << 17,

    /// <summary>
    /// Sprites of the third army.
    /// </summary>
    Army2 = 1 << 18,

    /// <summary>
    /// Sprites of the fourth army.
    /// </summary>
    Army3 = 1 << 19,

    /// <summary>
    /// Sprites of any army.
    /// </summary>
    AnyArmy = Army0 | Army1 | Army2 | Army3,

    /// <summary>
    /// Terrain sprites.
    /// </summary>
    Terrain = 1 << 20,

    /// <summary>
    /// Object sprites.
    /// </summary>
    Object = 1 << 21,

    /// <summary>
    /// Ground units.
    /// </summary>
    Unit = 1 << 22,

    /// <summary>
    /// Flying units.
    /// </summary>
    Avia = 1 << 23,

    /// <summary>
    /// Menu sprites.
    /// </summary>
    Menu = 1 << 24,

    /// <summary>
    /// Railway sprites.
    /// </summary>
    Railway = 1 << 25,

    /// <summary>
    /// Region sprites.
    /// </summary>
    Region = 1 << 26,

    /// <summary>
    /// Projectiles and effects.
    /// </summary>
    Cannon = 1 << 29,

    /// <summary>
    /// Attached sprites.
    /// </summary>
    Sprite = 1 << 30,

    /// <summary>
    /// Sprites that don't carry a command.
    /// </summary>
    Free = 1 << 31
}

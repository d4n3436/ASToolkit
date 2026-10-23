namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the sprite class of an entry of objects.res.
/// </summary>
public enum SpriteClass : uint
{
    /// <summary>
    /// A terrain sprite.
    /// </summary>
    Terrain = 0,

    /// <summary>
    /// An object.
    /// </summary>
    Object = 1,

    /// <summary>
    /// A unit.
    /// </summary>
    Unit = 2,

    /// <summary>
    /// A building.
    /// </summary>
    Building = 3,

    /// <summary>
    /// A flying unit.
    /// </summary>
    Avia = 4,

    /// <summary>
    /// A shell.
    /// </summary>
    Cannon = 5,

    /// <summary>
    /// The simplest and fastest sprite to process.
    /// </summary>
    Primitive = 6,

    /// <summary>
    /// A sprite.
    /// </summary>
    Sprite = 9,

    /// <summary>
    /// An interface frame.
    /// </summary>
    Frame = 10,

    /// <summary>
    /// A linker.
    /// </summary>
    Linker = 12,

    /// <summary>
    /// A text.
    /// </summary>
    Text = 19,

    /// <summary>
    /// A civilian robot.
    /// </summary>
    CivRobot = 20,

    /// <summary>
    /// An engine.
    /// </summary>
    Engine = 21,

    /// <summary>
    /// A rail.
    /// </summary>
    Rail = 22,

    /// <summary>
    /// A region.
    /// </summary>
    Region = 23,

    /// <summary>
    /// A depot.
    /// </summary>
    Depo = 24,

    /// <summary>
    /// A creature.
    /// </summary>
    Creature = 25,

    /// <summary>
    /// A balloon.
    /// </summary>
    Balloon = 26,

    /// <summary>
    /// A missile.
    /// </summary>
    Missile = 27
}

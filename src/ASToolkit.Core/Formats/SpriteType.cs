namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the sprite type of an entry of objects.res in the Alien Shooter 2 numbering.
/// </summary>
[Flags]
public enum SpriteType : uint
{
    /// <summary>
    /// No type.
    /// </summary>
    None = 0,

    /// <summary>
    /// A placeholder that spawns other objects or anchors scripts.
    /// </summary>
    Dummy = 1 << 0,

    /// <summary>
    /// The ground or a landscape element.
    /// </summary>
    Terrain = 1 << 1,

    /// <summary>
    /// An object.
    /// </summary>
    Object = 1 << 2,

    /// <summary>
    /// A ground unit or monster.
    /// </summary>
    Unit = 1 << 3,

    /// <summary>
    /// A flying unit.
    /// </summary>
    Avia = 1 << 4,

    /// <summary>
    /// A menu element.
    /// </summary>
    Menu = 1 << 5,

    /// <summary>
    /// A rail, path or pipe.
    /// </summary>
    Railway = 1 << 6,

    /// <summary>
    /// A rectangular region of water, fog or similar.
    /// </summary>
    Region = 1 << 7,

    /// <summary>
    /// A shell, smoke or similar effect.
    /// </summary>
    Cannon = 1 << 9,

    /// <summary>
    /// A sprite that is mostly linked to other objects.
    /// </summary>
    Sprite = 1 << 10
}

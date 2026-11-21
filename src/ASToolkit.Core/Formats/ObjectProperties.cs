namespace ASToolkit.Core.Formats;

/// <summary>
/// Specifies the properties of an entry of objects.res.
/// </summary>
[Flags]
public enum ObjectProperties
{
    /// <summary>
    /// No properties.
    /// </summary>
    None = 0,

    /// <summary>
    /// The object is born with a random direction (casings, craters, holes, dried blood).
    /// </summary>
    RandomBirth = 1 << 0,

    /// <summary>
    /// The object falls with the first gravity constant.
    /// </summary>
    Gravity = 1 << 1,

    /// <summary>
    /// The object falls with the second gravity constant. This property and <see cref="Gravity"/> also mark the object as moving.
    /// </summary>
    Gravity2 = 1 << 2,

    /// <summary>
    /// The size of the object is built into the height grid of the map, which blocks movement along its footprint outline.
    /// </summary>
    BuildSizeToGridZ = 1 << 3,

    /// <summary>
    /// The animation step creates children and runs the queued actions of the object on every frame.
    /// </summary>
    Track = 1 << 4,

    /// <summary>
    /// The depth of the sprite is built into the height grid of the map, which blocks movement.
    /// </summary>
    BuildVidZToGridZ = 1 << 5,

    /// <summary>
    /// The object is indexed for hit and blast queries, so it can be found, damaged and block movement.
    /// </summary>
    Hash = 1 << 6,

    /// <summary>
    /// The object is born at the times that make a continuous line of smoke (flame, gun smoke); it plays while it is alive.
    /// </summary>
    BirthAsSmoke = 1 << 7,

    /// <summary>
    /// The object is born scattered around its spawn position. Alien Shooter 2 reads it as a footprint box that rotates with the direction.
    /// </summary>
    Noise = 1 << 8,

    /// <summary>
    /// The object takes its height from the height grid of the map.
    /// </summary>
    ZeroZ = 1 << 9,

    /// <summary>
    /// The object starts with a random speed up to the speed in its record. Only the original game reads it, and no object sets it.
    /// </summary>
    RandomSpeed = 1 << 10,

    /// <summary>
    /// The ambient gamma of the level does not apply to the object.
    /// </summary>
    Gamma = 1 << 11,

    /// <summary>
    /// The level's wind moves the object. In Alien Shooter 2 the object casts shadows, and an omni-light with the property draws them.
    /// </summary>
    Wind = 1 << 12,

    /// <summary>
    /// The sprite keeps one frame and the draw rotates it by the direction instead of switching to a direction frame (Alien Shooter 2).
    /// </summary>
    FreeRotation = 1 << 13,

    /// <summary>
    /// The object receives a touch message when another object overlaps it.
    /// </summary>
    Crush = 1 << 14,

    /// <summary>
    /// The object is drawn over everything below it, as items are. Alien Shooter 2 lifts it by 1000 units of height.
    /// </summary>
    AlwaysTop = 1 << 15,

    /// <summary>
    /// The object bobs up and down.
    /// </summary>
    Wave = 1 << 16,

    /// <summary>
    /// The object is hidden unless its army matches the army of the player.
    /// </summary>
    InvisibleForEnemy = 1 << 17,

    /// <summary>
    /// Child objects are created at the last frame of each state instead of the first.
    /// </summary>
    CreateChildEnd = 1 << 18,

    /// <summary>
    /// The direction frames of a falling object show its direction in the vertical plane.
    /// </summary>
    VerticalDirection = 1 << 19,

    /// <summary>
    /// The object moves straight toward its goal instead of along its direction.
    /// </summary>
    Homing = 1 << 20,

    /// <summary>
    /// The sprite is stretched into a beam between the object and its target.
    /// </summary>
    Blur = 1 << 21,

    /// <summary>
    /// The object starts with a random vertical speed, up or down, up to its vertical speed.
    /// </summary>
    RandomZSpeed = 1 << 22,

    /// <summary>
    /// An Alien Shooter omni-light that adds twice its light.
    /// </summary>
    DoubleLight = 1 << 23,

    /// <summary>
    /// The animation of the object starts on frame 0 instead of a random frame.
    /// </summary>
    OnePhase = 1 << 24,

    /// <summary>
    /// A spawned or attached object keeps its offset from the parent instead of rotating it with the parent's direction.
    /// </summary>
    FixedOffset = 1 << 25,

    /// <summary>
    /// The object damages everything inside its radius when it hits something, less with distance.
    /// </summary>
    RadialDamage = 1 << 26,

    /// <summary>
    /// The object flies toward its target and follows the height of the target.
    /// </summary>
    SelfMoving = 1 << 27,

    /// <summary>
    /// The object bounces off the ground instead of stopping.
    /// </summary>
    Bouncing = 1 << 28,

    /// <summary>
    /// The sprite is turned on the ground plane by the direction, which turns it smoothly between direction frames (Alien Shooter 2).
    /// </summary>
    HardwareDirect = 1 << 29,

    /// <summary>
    /// The object is a flat ground decal (blood, craters, floor tiles); it is drawn on the ground layer.
    /// </summary>
    Ground = 1 << 30,

    /// <summary>
    /// A hit or blast from the object skips objects of its own army.
    /// </summary>
    NoFriendlyDamage = 1 << 31
}

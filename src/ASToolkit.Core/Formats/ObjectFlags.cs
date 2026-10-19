namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the flags of an entry of objects.res.
/// </summary>
[Flags]
public enum ObjectFlags : uint
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// The object is a pure effect (smoke, sparks, blood, casings) with no collision.
    /// </summary>
    Effect = 1 << 0,

    /// <summary>
    /// The object flies ballistically; its frames advance with its velocity.
    /// </summary>
    Flying = 1 << 1,

    /// <summary>
    /// Set by the engine at runtime when the object has a velocity.
    /// </summary>
    Velocity = 1 << 2,

    /// <summary>
    /// The animation loops; without the flag the object stops on its last frame.
    /// </summary>
    Loop = 1 << 4,

    /// <summary>
    /// The object blocks movement.
    /// </summary>
    Solid = 1 << 5,

    /// <summary>
    /// The object blocks movement until it is shot.
    /// </summary>
    Breakable = 1 << 6,

    /// <summary>
    /// The object is a continuous stream (flame, gun smoke); it plays while it is alive.
    /// </summary>
    Stream = 1 << 7,

    /// <summary>
    /// The object is a living entity. Not every monster has it (e.g. <see cref="KnownObject.Bat"/> and
    /// <see cref="KnownObject.Diver"/>), and it is also set on objects that are not monsters (the player,
    /// corpses, shadows, turret bases).
    /// </summary>
    Entity = 1 << 9,

    /// <summary>
    /// The object is a unique instance; the engine does not deduplicate it with other objects of the same ID.
    /// </summary>
    Unique = 1 << 11,

    /// <summary>
    /// Set by the engine at runtime while the object moves; the animation then follows its speed.
    /// </summary>
    Moving = 1 << 12,

    /// <summary>
    /// The object lives for a short time and then disappears.
    /// </summary>
    Ephemeral = 1 << 13,

    /// <summary>
    /// The object is drawn over everything below it, as items are.
    /// </summary>
    Raised = 1 << 15,

    /// <summary>
    /// The object bobs up and down.
    /// </summary>
    Bobbing = 1 << 16,

    /// <summary>
    /// The visibility of the object depends on the level. Only object 631 has it.
    /// </summary>
    LevelDependent = 1 << 17,

    /// <summary>
    /// The animation freezes on the death frame when the object is killed, instead of disappearing.
    /// </summary>
    DeathAnim = 1 << 18,

    /// <summary>
    /// Set by the engine at runtime; the sprite direction then comes from the object's facing.
    /// </summary>
    Facing = 1 << 19,

    /// <summary>
    /// Set by the engine at runtime; the meaning is unknown.
    /// </summary>
    Runtime20 = 1 << 20,

    /// <summary>
    /// The projectile is energy-based (plasma, lightning, acid) and damages along its path.
    /// </summary>
    Energy = 1 << 21,

    /// <summary>
    /// The object is affected by gravity.
    /// </summary>
    Gravity = 1 << 22,

    /// <summary>
    /// The object emits light; its sprite is a 2 x 2 omni-light.
    /// </summary>
    Light = 1 << 23,

    /// <summary>
    /// The animation of the object starts on frame 0 instead of a random frame.
    /// </summary>
    StartsOnFirstFrame = 1 << 24,

    /// <summary>
    /// Set by the engine at runtime; the meaning is unknown.
    /// </summary>
    Runtime25 = 1 << 25,

    /// <summary>
    /// The object explodes when it is destroyed or hits something (rockets, dynamite, barrels).
    /// </summary>
    Explosive = 1 << 26,

    /// <summary>
    /// The object is a rocket; it flies homing toward its target.
    /// </summary>
    Rocket = 1 << 27,

    /// <summary>
    /// The object is a piece of debris.
    /// </summary>
    Debris = 1 << 28,

    /// <summary>
    /// The object is a flat ground decal (blood, craters, floor tiles); it is drawn on the ground layer.
    /// </summary>
    GroundDecal = 1 << 30,

    /// <summary>
    /// The object is a projectile; it does not collide with other projectiles.
    /// </summary>
    Projectile = 0x80000000
}

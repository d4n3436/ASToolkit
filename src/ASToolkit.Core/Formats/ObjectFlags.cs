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
    /// The object starts with a random facing (casings, craters, holes, dried blood).
    /// </summary>
    RandomFacing = 1 << 0,

    /// <summary>
    /// The object can leave the ground and falls with the first gravity constant.
    /// </summary>
    Flying = 1 << 1,

    /// <summary>
    /// The object falls with the second gravity constant. This flag and <see cref="Flying"/> also mark the object as moving.
    /// </summary>
    Heavy = 1 << 2,

    /// <summary>
    /// The object blocks movement along its footprint outline, sampled every 8 units.
    /// </summary>
    Outline = 1 << 3,

    /// <summary>
    /// The animation loops; without the flag the object stops on its last frame.
    /// </summary>
    Loop = 1 << 4,

    /// <summary>
    /// The object blocks movement.
    /// </summary>
    Solid = 1 << 5,

    /// <summary>
    /// The object is indexed for hit and blast queries, so shots and explosions can reach it.
    /// </summary>
    Hittable = 1 << 6,

    /// <summary>
    /// The object is a continuous stream (flame, gun smoke); it plays while it is alive.
    /// </summary>
    Stream = 1 << 7,

    /// <summary>
    /// The footprint is a box that rotates with the facing. The original game scatters the spawn position by up to 8 units instead.
    /// </summary>
    OrientedBox = 1 << 8,

    /// <summary>
    /// The object takes its height from the level's height map.
    /// </summary>
    FollowsTerrain = 1 << 9,

    /// <summary>
    /// The object starts with a random speed up to the speed in its record. Only the original game reads it, and no object sets it.
    /// </summary>
    RandomSpeed = 1 << 10,

    /// <summary>
    /// The level lighting does not tint the object.
    /// </summary>
    Unlit = 1 << 11,

    /// <summary>
    /// The level's wind moves the object. In Alien Shooter 2 the object casts shadows, and an omni-light with the flag draws them.
    /// </summary>
    Windblown = 1 << 12,

    /// <summary>
    /// The sprite keeps one frame and the draw rotates it by the facing instead of switching to a direction frame.
    /// </summary>
    FreeRotation = 1 << 13,

    /// <summary>
    /// The object receives a touch message when another object overlaps it.
    /// </summary>
    Touchable = 1 << 14,

    /// <summary>
    /// The object is drawn over everything below it, as items are. Alien Shooter 2 lifts it by 1000 units of height.
    /// </summary>
    Raised = 1 << 15,

    /// <summary>
    /// The object bobs up and down.
    /// </summary>
    Bobbing = 1 << 16,

    /// <summary>
    /// The object is hidden unless its side matches the side of the player.
    /// </summary>
    SideDependent = 1 << 17,

    /// <summary>
    /// The animation freezes on the death frame when the object is killed, instead of disappearing.
    /// </summary>
    DeathAnim = 1 << 18,

    /// <summary>
    /// The facing follows the movement or the target instead of the stored facing.
    /// </summary>
    TracksMotion = 1 << 19,

    /// <summary>
    /// The object steers toward its target while it moves.
    /// </summary>
    Homing = 1 << 20,

    /// <summary>
    /// The sprite is stretched into a beam between the object and its target.
    /// </summary>
    Beam = 1 << 21,

    /// <summary>
    /// The object starts with a random vertical speed, up or down, up to its vertical velocity.
    /// </summary>
    RandomLift = 1 << 22,

    /// <summary>
    /// A flag Alien Shooter 2 tests in its movement and collision code, with no confirmed purpose.
    /// </summary>
    Flag23 = 1 << 23,

    /// <summary>
    /// The animation of the object starts on frame 0 instead of a random frame.
    /// </summary>
    StartsOnFirstFrame = 1 << 24,

    /// <summary>
    /// A spawned or attached object keeps its offset from the parent instead of rotating it with the parent's facing.
    /// </summary>
    FixedOffset = 1 << 25,

    /// <summary>
    /// The object damages everything inside its radius when it hits something.
    /// </summary>
    Explosive = 1 << 26,

    /// <summary>
    /// The object is a rocket; it flies toward its target and follows the height of the target.
    /// </summary>
    Rocket = 1 << 27,

    /// <summary>
    /// The object bounces off the ground instead of stopping.
    /// </summary>
    Bouncing = 1 << 28,

    /// <summary>
    /// The sprite is turned on the ground plane by the facing, which turns it smoothly between direction frames.
    /// </summary>
    Rotated = 1 << 29,

    /// <summary>
    /// The object is a flat ground decal (blood, craters, floor tiles); it is drawn on the ground layer.
    /// </summary>
    GroundDecal = 1 << 30,

    /// <summary>
    /// A hit or blast from the object skips objects of its own side.
    /// </summary>
    Projectile = (uint)1 << 31
}

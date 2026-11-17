using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Alien Shooter 2 layout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 704)]
public readonly struct GameObject
{
    /// <summary>
    /// Represents the number of animation states of an object.
    /// </summary>
    public const int StateCount = 17;

    internal GameObject(AlienShooterGameObject obj)
    {
        SpriteType = obj.SpriteType.ToSpriteType();
        SpriteClass = obj.SpriteClass;
        Properties = obj.Properties;
        MoveMask = obj.MoveMask;
        Size = obj.Size;
        MaxHp = obj.MaxHp;
        MaxInitialSpeed = 0;
        MaxInitialZSpeed = 0;
        LinkOffset = obj.LinkOffset;
        Link = obj.Link;
        StepHeight = obj.StepHeight;
        DropHeight = obj.DropHeight;
        Directions = obj.Directions;
        FrameCounts = obj.FrameCounts;
        Sounds = obj.Sounds;
        FrameDurations = obj.FrameDurations;
        Gamma = obj.Gamma;
        Scale = obj.Scale;
        MaxSpeed = obj.MaxSpeed;
        MaxZSpeed = obj.MaxZSpeed;
        Acceleration = obj.Acceleration;
        Deceleration = obj.Deceleration;
        RotationPeriod = obj.RotationPeriod;
        TopZ = obj.TopZ;
        WeaponIndex = obj.WeaponIndex;
        DeathRange = obj.DeathRange;
        DeathDamage = obj.DeathDamage;
        MaxDeathDamage = obj.DeathDamage;
        Lifetime = obj.Lifetime;
        ChildX = obj.ChildX;
        ChildY = obj.ChildY;
        ChildZ = obj.ChildZ;
        Children = obj.Children;
        ChildCounts = obj.ChildCounts;
    }

    internal GameObject(ZombieShooterGameObject obj)
    {
        SpriteType = obj.SpriteType.ToSpriteType();
        SpriteClass = obj.SpriteClass;
        Properties = obj.Properties;
        MoveMask = obj.MoveMask;
        Size = obj.Size;
        MaxHp = obj.MaxHp;
        MaxInitialSpeed = obj.MaxInitialSpeed;
        MaxInitialZSpeed = obj.MaxInitialZSpeed;
        LinkOffset = obj.LinkOffset;
        Link = obj.Link;
        StepHeight = obj.StepHeight;
        DropHeight = obj.DropHeight;
        Directions = obj.Directions;
        FrameCounts = obj.FrameCounts;
        Sounds = obj.Sounds;
        FrameDurations = obj.FrameDurations;
        Gamma = obj.Gamma;
        Scale = obj.Scale;
        MaxSpeed = obj.MaxSpeed;
        MaxZSpeed = obj.MaxZSpeed;
        Acceleration = obj.Acceleration;
        Deceleration = obj.Deceleration;
        RotationPeriod = obj.RotationPeriod;
        TopZ = obj.TopZ;
        WeaponIndex = obj.WeaponIndex;
        DeathRange = obj.DeathRange;
        DeathDamage = obj.DeathDamage;
        MaxDeathDamage = obj.DeathDamage;
        Lifetime = obj.Lifetime;
        ChildX = obj.ChildX;
        ChildY = obj.ChildY;
        ChildZ = obj.ChildZ;
        Children = obj.Children;
        ChildCounts = obj.ChildCounts;
    }

    internal GameObject(ZombieShooter2GameObject obj)
    {
        SpriteType = obj.SpriteType;
        SpriteClass = obj.SpriteClass;
        Properties = obj.Properties;
        MoveMask = obj.MoveMask;
        Size = obj.Size;
        MaxHp = obj.MaxHp;
        MaxInitialSpeed = obj.MaxInitialSpeed;
        MaxInitialZSpeed = obj.MaxInitialZSpeed;
        LinkOffset = obj.LinkOffset;
        Link = obj.Link;
        StepHeight = obj.StepHeight;
        DropHeight = obj.DropHeight;
        Directions = obj.Directions;
        FrameCounts = obj.FrameCounts;
        Sounds = obj.Sounds;
        FrameDurations = obj.FrameDurations;
        Gamma = obj.Gamma;
        Scale = obj.Scale;
        MaxSpeed = obj.MaxSpeed;
        MaxZSpeed = obj.MaxZSpeed;
        Acceleration = obj.Acceleration;
        Deceleration = obj.Deceleration;
        RotationPeriod = obj.RotationPeriod;
        TopZ = obj.TopZ;
        WeaponIndex = obj.WeaponIndex;
        DeathRange = obj.DeathRange;
        DeathDamage = obj.DeathDamage;
        MaxDeathDamage = obj.MaxDeathDamage;
        Push = obj.Push;
        Lifetime = obj.Lifetime;
        ChildX = obj.ChildX;
        ChildY = obj.ChildY;
        ChildZ = obj.ChildZ;
        Children = obj.Children;
        ChildCounts = obj.ChildCounts;
    }

    /// <summary>
    /// The sprite type of the object.
    /// </summary>
    [FieldOffset(0)]
    public readonly SpriteType SpriteType;

    /// <summary>
    /// The sprite class of the object.
    /// </summary>
    [FieldOffset(4)]
    public readonly SpriteClass SpriteClass;

    /// <summary>
    /// The properties of the object.
    /// </summary>
    [FieldOffset(8)]
    public readonly ObjectProperties Properties;

    /// <summary>
    /// The collision groups of the object. Two objects collide when their masks share a bit.
    /// </summary>
    [FieldOffset(12)]
    public readonly CollisionLayers MoveMask;

    /// <summary>
    /// The size of the object along x, y and the height.
    /// </summary>
    [FieldOffset(16)]
    public readonly Vector3 Size;

    /// <summary>
    /// The maximum hit points.
    /// </summary>
    [FieldOffset(28)]
    public readonly int MaxHp;

    /// <summary>
    /// The highest speed of the object along its facing and the initial speed of a spawned object.
    /// </summary>
    [FieldOffset(32)]
    public readonly float MaxSpeed;

    /// <summary>
    /// The maximum of the initial speed range, in milli-units; the highest speed is the minimum. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(36)]
    public readonly float MaxInitialSpeed;

    /// <summary>
    /// The highest and initial vertical speed of the object in milli-units.
    /// </summary>
    [FieldOffset(40)]
    internal readonly float MaxZSpeed;

    /// <summary>
    /// The maximum of the initial vertical speed range, in milli-units. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(44)]
    public readonly float MaxInitialZSpeed;

    /// <summary>
    /// The stored forward acceleration, scaled by 0.000001 on load except for the instant value 999999.
    /// </summary>
    [FieldOffset(48)]
    public readonly float Acceleration;

    /// <summary>
    /// The stored forward deceleration, scaled by 0.000001 on load except for the instant value 999999.
    /// </summary>
    [FieldOffset(52)]
    public readonly float Deceleration;

    /// <summary>
    /// The duration of a full turn in milliseconds. Zero turns instantly and 999999 disables turning.
    /// </summary>
    [FieldOffset(56)]
    public readonly float RotationPeriod;

    /// <summary>
    /// The index of the WEAP record of the object, or zero for none.
    /// </summary>
    [FieldOffset(60)]
    public readonly int WeaponIndex;

    /// <summary>
    /// The radius the object's blast adds to its footprint when it dies or hits something.
    /// </summary>
    [FieldOffset(64)]
    public readonly float DeathRange;

    /// <summary>
    /// The damage the object sends to everything its blast reaches.
    /// </summary>
    [FieldOffset(68)]
    public readonly int DeathDamage;

    /// <summary>
    /// The highest damage the object's blast deals. Absent from Alien Shooter and Zombie Shooter records.
    /// </summary>
    [FieldOffset(72)]
    public readonly int MaxDeathDamage;

    /// <summary>
    /// The push of the object's blast. Absent from Alien Shooter and Zombie Shooter records.
    /// </summary>
    [FieldOffset(76)]
    public readonly float Push;

    /// <summary>
    /// The offset of the linked object from this object.
    /// </summary>
    [FieldOffset(80)]
    public readonly Vector3 LinkOffset;

    /// <summary>
    /// The ID of the object the engine creates linked to this object, or zero for none.
    /// </summary>
    [FieldOffset(92)]
    public readonly KnownObject Link;

    /// <summary>
    /// The height above the terrain the object keeps while it is not falling. Negative values also affect projectile motion.
    /// </summary>
    [FieldOffset(96)]
    public readonly float TopZ;

    /// <summary>
    /// The highest step the object climbs, and the height a bobbing object rises and sinks by.
    /// </summary>
    [FieldOffset(100)]
    public readonly float StepHeight;

    /// <summary>
    /// The deepest drop the object walks off.
    /// </summary>
    [FieldOffset(104)]
    public readonly float DropHeight;

    /// <summary>
    /// The lifetime of the object in milliseconds; 999999 means forever.
    /// </summary>
    [FieldOffset(108)]
    public readonly int Lifetime;

    /// <summary>
    /// The number of directions the sprite has frames for. Every entry of the game has at least 1.
    /// </summary>
    [FieldOffset(128)]
    public readonly int Directions;

    /// <summary>
    /// The number of frames in each direction of each animation state.
    /// </summary>
    [FieldOffset(132)]
    public readonly StateValues<int> FrameCounts;

    /// <summary>
    /// The sound id of each animation state, an index into the SFX chunk.
    /// </summary>
    [FieldOffset(200)]
    public readonly StateValues<int> Sounds;

    /// <summary>
    /// The frame duration of each animation state in milliseconds, or 0 for the speed of the sprite.
    /// </summary>
    [FieldOffset(268)]
    public readonly StateValues<int> FrameDurations;

    /// <summary>
    /// The x offset of the object created in each state.
    /// </summary>
    [FieldOffset(336)]
    public readonly StateValues<float> ChildX;

    /// <summary>
    /// The y offset of the object created in each state.
    /// </summary>
    [FieldOffset(404)]
    public readonly StateValues<float> ChildY;

    /// <summary>
    /// The height offset of the object created in each state.
    /// </summary>
    [FieldOffset(472)]
    public readonly StateValues<float> ChildZ;

    /// <summary>
    /// The ID of the object created in each state.
    /// </summary>
    [FieldOffset(540)]
    public readonly StateValues<int> Children;

    /// <summary>
    /// The number of objects created in each state.
    /// </summary>
    [FieldOffset(608)]
    public readonly StateValues<int> ChildCounts;

    /// <summary>
    /// The gamma of the sprite, with a negative alpha on smoke, steam and shadows.
    /// </summary>
    [FieldOffset(676)]
    public readonly VidGamma Gamma;

    /// <summary>
    /// The scale of the object along x, y and the height.
    /// </summary>
    [FieldOffset(692)]
    public readonly Vector3 Scale;

    /// <summary>
    /// The number of frames of the standing animation in each direction.
    /// </summary>
    public int FramesPerDirection => FrameCounts[(int)SpriteAnimation.Stand];

    /// <summary>
    /// Gets a value indicating whether the object stops the player.
    /// </summary>
    public bool IsSolid => Properties.HasFlag(ObjectProperties.BuildVidZToGridZ);

    /// <summary>
    /// Gets a value indicating whether shots and explosions can reach the object.
    /// </summary>
    public bool IsHittable => Properties.HasFlag(ObjectProperties.Hash);

    /// <summary>
    /// Tells whether the object stands in the way of a walking unit as a box, which excludes scenery, units, aircraft, cannons,
    /// objects that break and non-solid helper dummies.
    /// </summary>
    /// <param name="walker">The walking unit.</param>
    public bool Blocks(GameObject walker) =>
        SpriteClass != SpriteClass.BuiltTerrain && (MoveMask & walker.MoveMask) != 0 && !SpriteType.HasFlag(SpriteType.Unit)
        && !(SpriteType.HasFlag(SpriteType.Dummy) && !IsSolid) && !SpriteType.HasFlag(SpriteType.Avia) && !SpriteType.HasFlag(SpriteType.Cannon)
        && !(IsHittable && MaxHp > 0);

    /// <summary>
    /// Returns the sprite frame the engine shows for a direction, out of the given number of frames.
    /// </summary>
    public int GetFrame(Angle dir, int frameCount) => FramesPerDirection == 0 ? 0 : Math.Min(dir.GetDirectionIndex(Directions) * FramesPerDirection, frameCount - 1);

    /// <summary>
    /// Returns the sprite frame that starts an animation state.
    /// </summary>
    public int GetStateStart(SpriteAnimation animation)
    {
        int start = 0;
        for (int previous = 0; previous < (int)animation; previous++)
        {
            start += FrameCounts[previous] * Directions;
        }

        return start;
    }

    /// <summary>
    /// Returns the frame duration of an animation state in milliseconds, given the speed of the sprite.
    /// </summary>
    public int GetFrameDuration(SpriteAnimation animation, int spriteSpeed) =>
        FrameDurations[(int)animation] == 0 ? spriteSpeed : FrameDurations[(int)animation];

    /// <summary>
    /// Returns the animation whose frames are drawn for a state, falling back to the first populated state.
    /// </summary>
    public SpriteAnimation GetVisualAnimation(SpriteAnimation animation)
    {
        if (FrameCounts[(int)animation] > 0)
            return animation;

        for (int index = 0; index < StateCount; index++)
        {
            if (FrameCounts[index] > 0)
                return (SpriteAnimation)index;
        }

        return animation;
    }

    /// <summary>
    /// Returns the offset of an attached object from the object, turned by a facing unless the attached object keeps a fixed offset.
    /// </summary>
    public Vector3 GetLinkOffset(GameObject attached, VidResource? file, int frameIndex, Angle dir)
    {
        var offset = file is not null && file.Value.Type.HasFlag(VidType.FrameVector)
            ? file.Value.GetFrameVector(frameIndex)
            : LinkOffset with { Y = -LinkOffset.Y };
        
        if (attached.Properties.HasFlag(ObjectProperties.FixedOffset))
        {
            return offset;
        }

        (float sin, float cos) = MathF.SinCos(dir.Value * MathF.PI / 128);
        return new Vector3(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, offset.Z);
    }
}

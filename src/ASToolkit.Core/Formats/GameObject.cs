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

    private const uint ShiftedTypeMask = 0x7F;
    private const uint SharedTypeMask = ~0xFFu;

    internal GameObject(AlienShooterGameObject obj)
    {
        SpriteType = ConvertSpriteType(obj.SpriteType);
        SpriteClass = obj.SpriteClass;
        Flags = obj.Flags;
        MoveMask = obj.MoveMask;
        Size = obj.Size;
        MaxHp = obj.MaxHp;
        MaxSpeed = 0;
        MaxZSpeed = 0;
        LinkOffset = obj.LinkOffset;
        Link = obj.Link;
        StepHeight = obj.StepHeight;
        DropHeight = obj.DropHeight;
        Directions = obj.Directions;
        FrameCounts = obj.FrameCounts;
        SoundIds = obj.SoundIds;
        FrameDurations = obj.FrameDurations;
        GammaRed = obj.GammaRed;
        GammaGreen = obj.GammaGreen;
        GammaBlue = obj.GammaBlue;
        GammaAlpha = obj.GammaAlpha;
        Scale = obj.Scale;
        Speed = obj.Speed;
        ZSpeed = obj.ZSpeed;
        Acceleration = obj.Acceleration;
        Deceleration = obj.Deceleration;
        TurnPeriod = obj.TurnPeriod;
        RestHeight = obj.RestHeight;
        WeaponIndex = obj.WeaponIndex;
        BlastRadius = obj.BlastRadius;
        Damage = obj.Damage;
        MaxDamage = obj.Damage;
        Lifetime = obj.Lifetime;
        ChildX = obj.ChildX;
        ChildY = obj.ChildY;
        ChildHeight = obj.ChildHeight;
        Children = obj.Children;
        ChildCounts = obj.ChildCounts;
    }

    internal GameObject(ZombieShooterGameObject obj)
    {
        SpriteType = ConvertSpriteType(obj.SpriteType);
        SpriteClass = obj.SpriteClass;
        Flags = obj.Flags;
        MoveMask = obj.MoveMask;
        Size = obj.Size;
        MaxHp = obj.MaxHp;
        MaxSpeed = obj.MaxSpeed;
        MaxZSpeed = obj.MaxZSpeed;
        LinkOffset = obj.LinkOffset;
        Link = obj.Link;
        StepHeight = obj.StepHeight;
        DropHeight = obj.DropHeight;
        Directions = obj.Directions;
        FrameCounts = obj.FrameCounts;
        SoundIds = obj.SoundIds;
        FrameDurations = obj.FrameDurations;
        GammaRed = obj.GammaRed;
        GammaGreen = obj.GammaGreen;
        GammaBlue = obj.GammaBlue;
        GammaAlpha = obj.GammaAlpha;
        Scale = obj.Scale;
        Speed = obj.Speed;
        ZSpeed = obj.ZSpeed;
        Acceleration = obj.Acceleration;
        Deceleration = obj.Deceleration;
        TurnPeriod = obj.TurnPeriod;
        RestHeight = obj.RestHeight;
        WeaponIndex = obj.WeaponIndex;
        BlastRadius = obj.BlastRadius;
        Damage = obj.Damage;
        MaxDamage = obj.Damage;
        Lifetime = obj.Lifetime;
        ChildX = obj.ChildX;
        ChildY = obj.ChildY;
        ChildHeight = obj.ChildHeight;
        Children = obj.Children;
        ChildCounts = obj.ChildCounts;
    }

    internal GameObject(ZombieShooter2GameObject obj)
    {
        SpriteType = obj.SpriteType;
        SpriteClass = obj.SpriteClass;
        Flags = obj.Flags;
        MoveMask = obj.MoveMask;
        Size = obj.Size;
        MaxHp = obj.MaxHp;
        MaxSpeed = obj.MaxSpeed;
        MaxZSpeed = obj.MaxZSpeed;
        LinkOffset = obj.LinkOffset;
        Link = obj.Link;
        StepHeight = obj.StepHeight;
        DropHeight = obj.DropHeight;
        Directions = obj.Directions;
        FrameCounts = obj.FrameCounts;
        SoundIds = obj.SoundIds;
        FrameDurations = obj.FrameDurations;
        GammaRed = obj.GammaRed;
        GammaGreen = obj.GammaGreen;
        GammaBlue = obj.GammaBlue;
        GammaAlpha = obj.GammaAlpha;
        Scale = obj.Scale;
        Speed = obj.Speed;
        ZSpeed = obj.ZSpeed;
        Acceleration = obj.Acceleration;
        Deceleration = obj.Deceleration;
        TurnPeriod = obj.TurnPeriod;
        RestHeight = obj.RestHeight;
        WeaponIndex = obj.WeaponIndex;
        BlastRadius = obj.BlastRadius;
        Damage = obj.Damage;
        MaxDamage = obj.MaxDamage;
        Push = obj.Push;
        Lifetime = obj.Lifetime;
        ChildX = obj.ChildX;
        ChildY = obj.ChildY;
        ChildHeight = obj.ChildHeight;
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
    /// The object flags.
    /// </summary>
    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

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
    /// The speed of the object along its facing.
    /// </summary>
    [FieldOffset(32)]
    public readonly float Speed;

    /// <summary>
    /// The maximum of the initial speed range, in milli-units; the speed is the minimum. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(36)]
    public readonly float MaxSpeed;

    /// <summary>
    /// The initial vertical speed in milli-units.
    /// </summary>
    [FieldOffset(40)]
    internal readonly float ZSpeed;

    /// <summary>
    /// The maximum of the initial vertical speed range, in milli-units. Absent from Alien Shooter records.
    /// </summary>
    [FieldOffset(44)]
    public readonly float MaxZSpeed;

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
    public readonly float TurnPeriod;

    /// <summary>
    /// Gets the index of the WEAP record of the object, or zero for none.
    /// </summary>
    [FieldOffset(60)]
    public readonly int WeaponIndex;

    /// <summary>
    /// Gets the blast radius the object adds to its footprint when it hits something.
    /// </summary>
    [FieldOffset(64)]
    public readonly float BlastRadius;

    /// <summary>
    /// Gets the damage the object sends to everything its blast reaches.
    /// </summary>
    [FieldOffset(68)]
    public readonly int Damage;

    /// <summary>
    /// Gets the highest damage the object's blast deals. Absent from Alien Shooter and Zombie Shooter records.
    /// </summary>
    [FieldOffset(72)]
    public readonly int MaxDamage;

    /// <summary>
    /// Gets the push of the object's blast. Absent from Alien Shooter and Zombie Shooter records.
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
    /// The resting height above terrain. Negative values also affect projectile motion.
    /// </summary>
    [FieldOffset(96)]
    public readonly float RestHeight;

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
    /// Gets the lifetime of the object in milliseconds; 999999 means forever.
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
    public readonly StateValues<int> SoundIds;

    /// <summary>
    /// The frame duration of each animation state in milliseconds, or 0 for the speed of the sprite.
    /// </summary>
    [FieldOffset(268)]
    public readonly StateValues<int> FrameDurations;

    /// <summary>
    /// Gets the x offset of the object created in each state.
    /// </summary>
    [FieldOffset(336)]
    public readonly StateValues<float> ChildX;

    /// <summary>
    /// Gets the y offset of the object created in each state.
    /// </summary>
    [FieldOffset(404)]
    public readonly StateValues<float> ChildY;

    /// <summary>
    /// Gets the height offset of the object created in each state.
    /// </summary>
    [FieldOffset(472)]
    public readonly StateValues<float> ChildHeight;

    /// <summary>
    /// Gets the ID of the object created in each state.
    /// </summary>
    [FieldOffset(540)]
    public readonly StateValues<int> Children;

    /// <summary>
    /// Gets the number of objects created in each state.
    /// </summary>
    [FieldOffset(608)]
    public readonly StateValues<int> ChildCounts;

    /// <summary>
    /// The gamma shift of the red channel of the sprite's palette.
    /// </summary>
    [FieldOffset(676)]
    public readonly int GammaRed;

    /// <summary>
    /// The gamma shift of the green channel of the sprite's palette.
    /// </summary>
    [FieldOffset(680)]
    public readonly int GammaGreen;

    /// <summary>
    /// The gamma shift of the blue channel of the sprite's palette.
    /// </summary>
    [FieldOffset(684)]
    public readonly int GammaBlue;

    /// <summary>
    /// The gamma shift of the alpha of the sprite; negative on smoke, steam and shadows.
    /// </summary>
    [FieldOffset(688)]
    public readonly int GammaAlpha;

    /// <summary>
    /// The scale of the object along x, y and the height.
    /// </summary>
    [FieldOffset(692)]
    public readonly Vector3 Scale;

    /// <summary>
    /// Gets the gamma shift applied to the sprite's palette per channel.
    /// </summary>
    public (int R, int G, int B) Gamma => (GammaRed, GammaGreen, GammaBlue);

    /// <summary>
    /// Gets the number of frames of the standing animation in each direction.
    /// </summary>
    public int FramesPerDirection => FrameCounts[(int)SpriteAnimation.Stand];

    /// <summary>
    /// Gets a value indicating whether the object stops the player.
    /// </summary>
    public bool IsSolid => Flags.HasFlag(ObjectFlags.Solid);

    /// <summary>
    /// Gets a value indicating whether shots and explosions can reach the object.
    /// </summary>
    public bool IsHittable => Flags.HasFlag(ObjectFlags.Hittable);

    /// <summary>
    /// Tells whether the object stands in the way of a walking unit as a box, which excludes scenery, units, aircraft, cannons,
    /// objects that break and non-solid helper dummies.
    /// </summary>
    /// <param name="walker">The walking unit.</param>
    public bool Blocks(GameObject walker) =>
        SpriteClass != SpriteClass.Scenery && (MoveMask & walker.MoveMask) != 0 && !SpriteType.HasFlag(SpriteType.Unit)
        && !(SpriteType.HasFlag(SpriteType.Dummy) && !IsSolid) && !SpriteType.HasFlag(SpriteType.Avia) && !SpriteType.HasFlag(SpriteType.Cannon)
        && !(IsHittable && MaxHp > 0);

    /// <summary>
    /// Returns the sprite frame the engine shows for a direction byte, out of the given number of frames.
    /// </summary>
    public int GetFrame(byte dir, int frameCount) => FramesPerDirection == 0 ? 0 : Math.Min(GetDirection(dir) * FramesPerDirection, frameCount - 1);

    /// <summary>
    /// Returns the index of the direction the engine picks for a direction byte.
    /// </summary>
    public int GetDirection(byte dir) => ((dir + 128 / Directions) & 255) * Directions >> 8;

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

        for (int index = 0; index < GameObject.StateCount; index++)
        {
            if (FrameCounts[index] > 0)
                return (SpriteAnimation)index;
        }

        return animation;
    }

    private static SpriteType ConvertSpriteType(OldSpriteType old) =>
        (SpriteType)((((uint)old & ShiftedTypeMask) << 1) | ((uint)old & SharedTypeMask));

    /// <summary>
    /// Returns the offset of an attached object from the object, turned by a facing unless the attached object keeps a fixed offset.
    /// </summary>
    public Vector3 GetLinkOffset(GameObject attached, VidResource? file, int frameIndex, byte dir)
    {
        var offset = file is not null && file.Value.Flags.HasFlag(VidFlags.FrameVector)
            ? file.Value.GetFrameVector(frameIndex)
            : LinkOffset with { Y = -LinkOffset.Y };
        
        if (attached.Flags.HasFlag(ObjectFlags.FixedOffset))
        {
            return offset;
        }

        (float sin, float cos) = MathF.SinCos(dir * MathF.PI / 128);
        return new Vector3(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, offset.Z);
    }
}

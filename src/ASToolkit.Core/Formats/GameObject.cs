using System.Numerics;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents the body of an entry of objects.res in the Alien Shooter 2 layout.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 704)]
public struct GameObject
{
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
        FrameDurations = obj.FrameDurations;
        GammaRed = obj.GammaRed;
        GammaGreen = obj.GammaGreen;
        GammaBlue = obj.GammaBlue;
        GammaAlpha = 0;
        Scale = obj.Scale;
        Opacity = 255;
        Speed = obj.Speed;
        WeaponIndex = obj.WeaponIndex;
        BlastRadius = obj.BlastRadius;
        Damage = obj.Damage;
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
        FrameDurations = obj.FrameDurations;
        GammaRed = obj.GammaRed;
        GammaGreen = obj.GammaGreen;
        GammaBlue = obj.GammaBlue;
        GammaAlpha = obj.GammaAlpha;
        Scale = obj.Scale;
        Opacity = 255;
        Speed = obj.Speed;
        WeaponIndex = obj.WeaponIndex;
        BlastRadius = obj.BlastRadius;
        Damage = obj.Damage;
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
        FrameDurations = obj.FrameDurations;
        GammaRed = obj.GammaRed;
        GammaGreen = obj.GammaGreen;
        GammaBlue = obj.GammaBlue;
        GammaAlpha = obj.GammaAlpha;
        Scale = obj.Scale;
        Opacity = obj.Opacity;
        Speed = obj.Speed;
        WeaponIndex = obj.WeaponIndex;
        BlastRadius = obj.BlastRadius;
        Damage = obj.Damage;
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
    public readonly uint MoveMask;

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
    /// The maximum of the initial speed range, in milli-units; the speed is the minimum.
    /// </summary>
    [FieldOffset(36)]
    public readonly float MaxSpeed;

    /// <summary>
    /// The maximum of the initial vertical speed range, in milli-units.
    /// </summary>
    [FieldOffset(44)]
    public readonly float MaxZSpeed;

    /// <summary>
    /// The opacity of the sprite, 0 to 255.
    /// </summary>
    [FieldOffset(48)]
    public int Opacity;

    /// <summary>
    /// The offset of the linked object from this object, as stored in the file.
    /// </summary>
    [FieldOffset(80)]
    public readonly Vector3 LinkOffset;

    /// <summary>
    /// The ID of the object the engine creates linked to this object, or zero for none.
    /// </summary>
    [FieldOffset(92)]
    public readonly KnownObject Link;

    /// <summary>
    /// Gets the speed of the object along its facing as stored; the engine scales it by 0.001 on load.
    /// </summary>
    [FieldOffset(32)]
    public readonly float Speed;

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
    /// Gets the lifetime of the object in milliseconds; 999999 means forever.
    /// </summary>
    [FieldOffset(108)]
    public readonly int Lifetime;

    /// <summary>
    /// Gets the x offset of the object created in each state.
    /// </summary>
    [FieldOffset(336)]
    public readonly StateFloats ChildX;

    /// <summary>
    /// Gets the y offset of the object created in each state.
    /// </summary>
    [FieldOffset(404)]
    public readonly StateFloats ChildY;

    /// <summary>
    /// Gets the height offset of the object created in each state.
    /// </summary>
    [FieldOffset(472)]
    public readonly StateFloats ChildHeight;

    /// <summary>
    /// Gets the ID of the object created in each state.
    /// </summary>
    [FieldOffset(540)]
    public readonly StateValues Children;

    /// <summary>
    /// Gets the number of objects created in each state.
    /// </summary>
    [FieldOffset(608)]
    public readonly StateValues ChildCounts;

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
    /// The number of directions the sprite has frames for. Every entry of the game has at least 1.
    /// </summary>
    [FieldOffset(128)]
    public readonly int Directions;

    /// <summary>
    /// The number of frames in each direction of each animation state.
    /// </summary>
    [FieldOffset(132)]
    public readonly StateValues FrameCounts;

    /// <summary>
    /// The frame duration of each animation state in milliseconds, or 0 for the speed of the sprite.
    /// </summary>
    [FieldOffset(268)]
    public readonly StateValues FrameDurations;

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
    /// The gamma shift of the alpha of the sprite; negative on smoke, steam and shadows. Zero in Alien Shooter 1.
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
    public int GetFrame(byte dir, int frameCount)
    {
        if (FramesPerDirection == 0)
        {
            return 0;
        }

        return Math.Min(GetDirection(dir) * FramesPerDirection, frameCount - 1);
    }

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

    private static SpriteType ConvertSpriteType(OldSpriteType old) =>
        (SpriteType)((((uint)old & ShiftedTypeMask) << 1) | ((uint)old & SharedTypeMask));

    /// <summary>
    /// Returns the offset of an attached object from the object, turned by a facing unless the attached object keeps a fixed offset.
    /// </summary>
    public readonly Vector3 GetLinkOffset(GameObject attached, VidResource? file, int frameIndex, byte dir)
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

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
    /// Creates a new instance of the <see cref="GameObject"/> structure from an object of the Alien Shooter 1 layout.
    /// </summary>
    /// <param name="old">The object.</param>
    internal GameObject(OldGameObject old)
    {
        Group = old.Group;
        Type = old.Type;
        Flags = old.Flags;
        CollisionMask = old.CollisionMask;
        ExtentX = old.ExtentX;
        ExtentY = old.ExtentY;
        ExtentZ = old.ExtentZ;
        HitPoints = old.HitPoints;
        AttachedOffsetX = old.AttachedOffsetX;
        AttachedOffsetY = old.AttachedOffsetY;
        AttachedOffsetZ = old.AttachedOffsetZ;
        AttachedObject = old.AttachedObject;
        Directions = old.Directions;
        FramesPerDirection = old.FramesPerDirection;
        TintRed = old.TintRed;
        TintGreen = old.TintGreen;
        TintBlue = old.TintBlue;
        TintAlpha = 0;
        ScaleX = old.ScaleX;
        ScaleY = old.ScaleY;
        ScaleZ = old.ScaleZ;
    }

    /// <summary>
    /// The group of the object, which holds its category in the low byte and its kind in the next one.
    /// </summary>
    [FieldOffset(0)]
    public readonly uint Group;

    /// <summary>
    /// The engine behavior class of the object.
    /// </summary>
    [FieldOffset(4)]
    public readonly uint Type;

    /// <summary>
    /// The object flags.
    /// </summary>
    [FieldOffset(8)]
    public readonly ObjectFlags Flags;

    /// <summary>
    /// The collision groups of the object. Two objects collide when their masks share a bit.
    /// </summary>
    [FieldOffset(12)]
    public readonly uint CollisionMask;

    /// <summary>
    /// The collision extent along x.
    /// </summary>
    [FieldOffset(16)]
    public readonly float ExtentX;

    /// <summary>
    /// The collision extent along y.
    /// </summary>
    [FieldOffset(20)]
    public readonly float ExtentY;

    /// <summary>
    /// The height.
    /// </summary>
    [FieldOffset(24)]
    public readonly float ExtentZ;

    /// <summary>
    /// The hit points.
    /// </summary>
    [FieldOffset(28)]
    public readonly int HitPoints;

    /// <summary>
    /// The x offset of the attached object from this one.
    /// </summary>
    [FieldOffset(80)]
    public readonly float AttachedOffsetX;

    /// <summary>
    /// The y offset of the attached object from this one, as stored in the file.
    /// </summary>
    [FieldOffset(84)]
    public readonly float AttachedOffsetY;

    /// <summary>
    /// The height offset of the attached object from this one.
    /// </summary>
    [FieldOffset(88)]
    public readonly float AttachedOffsetZ;

    /// <summary>
    /// The ID of the object the engine creates attached to this one, or zero for none.
    /// </summary>
    [FieldOffset(92)]
    public readonly KnownObject AttachedObject;

    /// <summary>
    /// The number of directions the sprite has frames for. Every entry of the game has at least 1.
    /// </summary>
    [FieldOffset(128)]
    public readonly int Directions;

    /// <summary>
    /// The number of frames of the first animation in each direction.
    /// </summary>
    [FieldOffset(132)]
    public readonly int FramesPerDirection;

    /// <summary>
    /// The color shift of the red channel of the sprite's palette.
    /// </summary>
    [FieldOffset(676)]
    public readonly int TintRed;

    /// <summary>
    /// The color shift of the green channel of the sprite's palette.
    /// </summary>
    [FieldOffset(680)]
    public readonly int TintGreen;

    /// <summary>
    /// The color shift of the blue channel of the sprite's palette.
    /// </summary>
    [FieldOffset(684)]
    public readonly int TintBlue;

    /// <summary>
    /// The shift of the alpha of the sprite; a negative value makes it more transparent. Alien Shooter 2 only.
    /// </summary>
    [FieldOffset(688)]
    public readonly int TintAlpha;

    /// <summary>
    /// The scale of the object along x.
    /// </summary>
    [FieldOffset(692)]
    public readonly float ScaleX;

    /// <summary>
    /// The scale of the object along y.
    /// </summary>
    [FieldOffset(696)]
    public readonly float ScaleY;

    /// <summary>
    /// The scale of the object along the height.
    /// </summary>
    [FieldOffset(700)]
    public readonly float ScaleZ;

    /// <summary>
    /// Gets the category of the object, the low byte of the group.
    /// </summary>
    public byte Category => (byte)Group;

    /// <summary>
    /// Gets the kind of the object, the second byte of the group.
    /// </summary>
    public byte Kind => (byte)(Group >> 8);

    /// <summary>
    /// Gets the color shift applied to the sprite's palette per channel; a negative value darkens and a positive one adds.
    /// </summary>
    public (int R, int G, int B) Tint => (TintRed, TintGreen, TintBlue);

    /// <summary>
    /// Gets the scale of the object.
    /// </summary>
    public Vector3 Scale => new(ScaleX, ScaleY, ScaleZ);

    /// <summary>
    /// Gets a value indicating whether the object stops the player.
    /// </summary>
    public bool IsSolid => Flags.HasFlag(ObjectFlags.Solid);

    /// <summary>
    /// Gets a value indicating whether shots and explosions can reach the object.
    /// </summary>
    public bool IsHittable => Flags.HasFlag(ObjectFlags.Hittable);

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
}

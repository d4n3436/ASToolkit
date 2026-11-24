using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a linker sprite.
/// </summary>
public sealed class Linker : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Linker"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0040ec60)]
    public Linker(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        Base = parent;
        StartDirection = direction;

        if (parent is null)
            return;

        parent.AddLinkToLast(this);
        Offset = position - parent.Position;
    }

    /// <summary>
    /// Gets or sets the linker's offset from its base when it was created.
    /// </summary>
    public Vector3 Offset { get; set; }

    /// <summary>
    /// Gets or sets the direction the linker faced when it was created.
    /// </summary>
    public Angle StartDirection { get; set; }

    /// <summary>
    /// Gets or sets the sprite the linker turns around.
    /// </summary>
    public Sprite? Base { get; set; }

    [EngineRoutine(0x0040ed10)]
    internal override void Destruct()
    {
        BreakLink();
        base.Destruct();
    }

    [EngineRoutine(0x00442e50)]
    internal void LinkRotate(Angle direction)
    {
        var origin = Base ?? Owner;

        if (origin is null)
            return;

        var turn = new Angle(unchecked((byte)(direction.Value - StartDirection.Value)));
        var turned = new Vector2(turn.Cosine, turn.ProjectedSine) * Offset.X + new Vector2(-turn.Sine, turn.ProjectedCosine) * Offset.Y;
        ChangeCoor(new Vector3(origin.Position.AsVector2() + turned, Position.Z));
    }
}

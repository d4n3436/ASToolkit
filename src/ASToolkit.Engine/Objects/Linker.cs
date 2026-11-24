using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a linker sprite.
/// </summary>
public sealed class Linker : Sprite
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Linker"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    public Linker(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null)
        : base(number, definition, position, army, hitPoints, direction, map)
    {
    }

    /// <summary>
    /// Gets or sets the sprite the linker turns around, the parent it was created with.
    /// </summary>
    public Sprite? Base { get; set; }

    /// <summary>
    /// Gets or sets the linker's offset from its base when it was created.
    /// </summary>
    public Vector3 Offset { get; set; }

    /// <summary>
    /// Gets or sets the direction the linker faced when it was created.
    /// </summary>
    public Angle StartDirection { get; set; }

    internal override void Destruct(Map map) => map.DestructLinker(this);

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

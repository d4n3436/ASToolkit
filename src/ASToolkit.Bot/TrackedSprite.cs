using System.Numerics;
using ASToolkit.Engine;

namespace ASToolkit.Bot;

internal sealed class TrackedSprite
{
    public TrackedSprite(Sprite sprite, bool canMove)
    {
        Sprite = sprite;
        CanMove = canMove;
        Position = sprite.Position;
        WasSolid = WalkField.IsSolid(sprite);
    }

    public Sprite Sprite { get; }

    public bool CanMove { get; }

    public bool IsGone { get; set; }

    public Vector3 Position { get; set; }

    public bool WasSolid { get; set; }
}

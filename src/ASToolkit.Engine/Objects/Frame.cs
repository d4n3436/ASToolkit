using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a menu frame sprite.
/// </summary>
public class Frame : Sprite
{
    private const int FirstButtonAction = 6;

    /// <summary>
    /// Initializes a new instance of the <see cref="Frame"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x00448a60)]
    public Frame(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, new Vector3(position.AsVector2() + map.Camera, position.Z), direction, parent, map)
    {
        Map.Menu.Insert(this);
    }

    [EngineRoutine(0x00448af0)]
    internal override void Destruct()
    {
        if (Map.Menu.Current == this)
        {
            Map.Menu.Current = null;
        }

        Map.Menu.Delete(this);
        Map.DeletePointerToSprite(this);
        base.Destruct();
    }

    [EngineRoutine(0x00448b30)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        if ((int)act < 0 || (int)act >= FirstButtonAction && act != ActionCode.NextCommand)
            return base.Action(act, var1, var2, var3, target);

        if ((int)act < FirstButtonAction || Animation > SpriteAnimation.Birth)
            return 0;

        var state = (MenuAnimation)Animation;
        if (state is MenuAnimation.PressDown or MenuAnimation.PressUp)
        {
            ChangeAnimation((SpriteAnimation)(state == MenuAnimation.PressDown ? MenuAnimation.SelectDown : MenuAnimation.SelectUp));
            HasPlayedStateSound = true;
        }

        if (Animation == SpriteAnimation.Birth)
        {
            ChangeAnimation(SpriteAnimation.Stand);
        }

        return 0;
    }
}

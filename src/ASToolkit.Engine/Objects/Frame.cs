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
    /// Initializes a new instance of the <see cref="Frame"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Frame(int number, Vid vid, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, vid, position, army, hitPoints, map, direction)
    {
    }

    [EngineRoutine(0x00448af0)]
    internal override void Destruct()
    {
        if (Map.MenuCurrent == this)
        {
            Map.MenuCurrent = null;
        }

        Map.RemoveSprite(Map.MenuList, this);
        Map.DeletePointerToSprite(this);
        base.Destruct();
    }

    [EngineRoutine(0x00448b30)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        if ((int)code < 0 || (int)code >= FirstButtonAction && code != ActionCode.NextCommand)
            return base.Action(act, var1, var2, var3, target);

        if ((int)code < FirstButtonAction || Animation > SpriteAnimation.Birth)
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

using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    internal const int ControlledPlayer = 0;


    /// <summary>
    /// Sends an action to a sprite.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="code">The action.</param>
    /// <param name="var1">The first argument.</param>
    /// <param name="var2">The second argument.</param>
    /// <param name="var3">The third argument.</param>
    /// <param name="target">The sprite argument.</param>
    /// <returns>The action's result.</returns>
    public static int Action(Sprite sprite, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0, Sprite? target = null) =>
        sprite.Action((int)code, var1, var2, var3, target);

    [EngineRoutine(0x00442a10)]
    internal void AddToUpdates(Sprite sprite)
    {
        if (sprite.Link is not null)
        {
            AddToUpdates(sprite.Link);
        }

        AddToLayer(sprite);
    }

    [EngineRoutine(0x00445fb0)]
    internal void Attack(Sprite sprite, Sprite? target)
    {
        var definition = sprite.Definition;

        if (definition.Class == SpriteClass.Linker && definition.FightChild != 0)
        {
            var state = sprite.Animation;
            sprite.SetAnimationValue(SpriteAnimation.Fight);
            SpriteKinematics.SetCommand(sprite, Command.OneAttack, target);
            CreateChild(sprite);
            sprite.SetAnimationValue(state);
            SpriteKinematics.SetCommand(sprite, Command.None);

            return;
        }

        // TODO: Port the trains; the IsTrain action of the attack test answers for every class
        if (target is null || target.Definition == Definitions.Empty || sprite.CanAttackThisSprite(target) || definition.Class == SpriteClass.Cannon ||
            Action(sprite, ActionCode.IsTrain) != 0)
        {
            SpriteKinematics.SetCommand(sprite, Command.Attack, target);
        }
    }

    [EngineRoutine(0x004429f0)]
    internal bool RemoveFromUpdates(Sprite sprite)
    {
        if (sprite.Link is not null)
        {
            RemoveFromUpdates(sprite.Link);
        }

        return _updates.Remove(sprite.Definition.Layer, sprite);
    }

}

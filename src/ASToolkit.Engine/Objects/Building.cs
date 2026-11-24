using System.Drawing;
using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a building sprite.
/// </summary>
public sealed class Building : Unit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Building"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Building(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, definition, position, army, hitPoints, map, direction)
    {
    }

    internal PendingBuild? PendingBuild { get; set; }

    [EngineRoutine(0x0044ca70)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.BackupCommand when Command == Command.Build && PendingBuild.HasValue:
                ActionStack.Push(new Act((int)ActionCode.BuildUnit, (int)PendingBuild.Value.Definition.Id, 0, 0));
                SetCommand(Command.None);

                return 0;
            case ActionCode.BuildUnit:
                bool noItem = false;

                if (var1 == 0)
                {
                    var1 = Map.Action(this, ActionCode.GetItemType, (int)BuiltSpriteType);
                    noItem = var1 == 0;
                }

                var definition = var1 >= 0 ? Map.Definitions.Find((KnownObject)var1) : null;

                if (!(noItem || Command == Command.None) || definition is null)
                    return 0;

                PendingBuild = new PendingBuild(definition, new Point(var2, var3));
                Timer = definition.BuildTime;
                SetCommand(Command.Build);

                if (Animation < SpriteAnimation.Death)
                {
                    ChangeAnimation(SpriteAnimation.StopMove);
                }

                return 0;
            case ActionCode.NextCommand:
                if (Animation >= SpriteAnimation.Death)
                    return 0;

                bool isBuilding = Command == Command.Build;

                if (isBuilding)
                {
                    ChangeAnimation(SpriteAnimation.StopMove);
                }
                else if (Animation != SpriteAnimation.StandOpen)
                {
                    ChangeAnimation(SpriteAnimation.Stand);
                }

                if (!isBuilding || Timer != 0 || !PendingBuild.HasValue)
                    return 0;

                var pending = PendingBuild.Value;

                var position = Map.GetBuildPosition(this, pending.Offset);

                if (!Map.IsFree(pending.Definition, position))
                    return 0;

                SetCommand(Command.None);
                AddBuiltUnit(pending.Definition, position);
                PendingBuild = null;
                ChangeAnimation(SpriteAnimation.Stand);

                return 0;
        }

        return base.Action(act, var1, var2, var3, target);
    }

    internal override void MoveTact()
    {
        // TODO: Port the call of 0x44be50 that 0x44ca50 makes for object 104
    }
}

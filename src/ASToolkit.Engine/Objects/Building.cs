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
    /// Initializes a new instance of the <see cref="Building"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044ca00)]
    public Building(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        PendingBuild = null;
    }

    internal PendingBuild? PendingBuild { get; set; }

    [EngineRoutine(0x0044ca70)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        switch (act)
        {
            case ActionCode.BackupCommand when Command == Command.Build && PendingBuild.HasValue:
                ActionStack.Insert(new Act((int)ActionCode.BuildUnit, (int)PendingBuild.Value.Vid.Id, 0, 0));
                SetCommand(Command.None);

                return 0;
            case ActionCode.BuildUnit:
                bool noItem = false;

                if (var1 == 0)
                {
                    var1 = Map.Action(this, ActionCode.GetItemType, (int)BuiltSpriteType);
                    noItem = var1 == 0;
                }

                var vid = var1 >= 0 ? Map.Vids.Find((KnownObject)var1) : null;

                if (!(noItem || Command == Command.None) || vid is null)
                    return 0;

                PendingBuild = new PendingBuild(vid, new Point(var2, var3));
                Timer = vid.BuildTime;
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

                if (!Map.IsFree(pending.Vid, position))
                    return 0;

                SetCommand(Command.None);
                AddBuiltUnit(pending.Vid, position);
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

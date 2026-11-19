using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private const int PlayerIdLimit = 20;
    private const int ControlledPlayer = 0;
    private const int HpShift = 8;
    private const int HpRounding = 0xff;
    private const float SpeedPerMillisecond = 0.001f;

    /// <summary>
    /// Sends an action to a sprite through the handler of its sprite class.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="code">The action.</param>
    /// <param name="var1">The first argument.</param>
    /// <param name="var2">The second argument.</param>
    /// <param name="var3">The third argument.</param>
    /// <param name="target">The sprite argument.</param>
    /// <returns>The action's result.</returns>
    [EngineRoutine(0x00454800)]
    [EngineRoutine(0x0044ca70)]
    public int Action(Sprite sprite, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0, Sprite? target = null)
    {
        switch (sprite.Definition.Object.SpriteClass)
        {
            case SpriteClass.Man:
                switch (code)
                {
                    case ActionCode.CoorAttack:
                        Weapons.AimPlayer(new Vector2(var1, var2));

                        return 0;
                    case ActionCode.ChangeVid:
                        ChangePlayerVid(var1, var2);

                        return 0;
                    case ActionCode.AddItem:
                        AddPlayerItem(sprite, var1);

                        return 0;
                }

                break;
            case SpriteClass.Building:
                switch (code)
                {
                    case ActionCode.BuildUnit:
                        StartBuild(sprite, var1, var2, var3);

                        return 0;
                    case ActionCode.NextCommand:
                        DecideBuilding(sprite);

                        return 0;
                }

                break;
        }

        return UnitAction(sprite, code, var1, var2, var3, target);
    }

    [EngineRoutine(0x00440830)]
    private int UnitAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
    {
        switch (code)
        {
            case ActionCode.Attack:
                MonsterBrain.Attack(sprite, target);
                break;
            case ActionCode.Move:
                DrawGoalSpriteRandom();
                Motion.SetMoveTarget(sprite, null, new Vector3(var1, var2, var3));
                break;
            case ActionCode.MoveTo:
                Motion.SetMoveTarget(sprite, target, null);
                break;
            case ActionCode.BuildUnit:
                Build(sprite, var1, var2, var3);
                break;
            case ActionCode.Stop:
                SpriteKinematics.Stop(sprite);

                if (var1 != 0)
                {
                    sprite.Speed = 0;
                }

                break;
            case ActionCode.Pause:
                MonsterBrain.Pause(sprite, var1 + _rng.Next() % (var2 + 1));
                break;
            case ActionCode.Turn:
                sprite.ChangeDirection(sprite.Direction.TurnToward(new Angle(unchecked((byte)var1)), sprite.Definition.Object.RotationPeriod,
                    sprite.Definition.Layout.GetFrameDuration(sprite.Animation), out _));
                break;
            case ActionCode.ClearCommand:
                SpriteKinematics.SetCommand(sprite, Command.None);
                break;
            case ActionCode.FlagmanTrigger:
                if (!Player.IsAlive || Player.Position.AsVector2().MeasureOctagonalDistance(new Vector2(var1, var2)) >= var3)
                {
                    sprite.ActionStack.Push(new Act((int)code, var1, var2, var3));
                }

                break;
            case ActionCode.ChangeVid:
                if (!sprite.IsAlive && (SpriteAnimation)var2 < SpriteAnimation.Death)
                {
                    sprite.Hp = sprite.HpBeforeDeath;
                }

                ChangeVid(sprite, var1, var2);
                break;
            case ActionCode.ChangeCoor:
                sprite.ChangeCoor(new Vector3(var1, var2, var3));
                break;
            case ActionCode.ChangeDirection:
                sprite.ChangeDirection(new Angle(unchecked((byte)var1)));
                break;
            case ActionCode.ChangeAnimation:
                sprite.ChangeAnimation((SpriteAnimation)var1);
                break;
            case ActionCode.GotoStack:
                sprite.ActionStack.SetCount(var1 + 1);
                sprite.Frame = sprite.LastFrame;
                break;
            case ActionCode.ClearStack:
                sprite.ActionStack.Clear();
                break;
            case ActionCode.StopStack:
                sprite.ActionStack.Push(new Act((int)code, var1, var2, var3));
                Action(sprite, ActionCode.NextCommand);
                break;
            case ActionCode.NextCommand:
                NextCommand(sprite);
                break;
            case ActionCode.DestroyUnit:
                DestroySprite((KnownObject)var1, new Vector2(var2, var3));
                break;
            case ActionCode.GetAmmo:
                return sprite.GetAmmo(var1);
            case ActionCode.AddAmmo:
                return sprite.AddAmmo(var1, var2);
            case ActionCode.AddItem:
                sprite.Items.Add(var1);
                break;
            case ActionCode.DeleteItem:
            {
                int i = sprite.Items.LastIndexOf(var1);

                if (i < 0)
                    break;

                sprite.Items[i] = sprite.Items[^1];
                sprite.Items.RemoveAt(sprite.Items.Count - 1);

                return 1;
            }
            case ActionCode.HaveItem:
                return sprite.Items.Contains(var1) ? 1 : 0;
            case ActionCode.DeleteAllItems:
                sprite.Items.Clear();
                break;
            case ActionCode.GetItem:
                return var1 >= 0 && var1 < sprite.Items.Count ? sprite.Items[var1] : 0;
            case ActionCode.Damage:
                Damage(sprite, var1);
                break;
            case ActionCode.GetHp:
                return sprite.Hp;
            case ActionCode.SetHp:
                ChangeHp(sprite, var1 != 0 ? var1 : sprite.Definition.ArmyMaxHp[(int)sprite.Army & ArmyMask] * var2 / Percent);
                break;
            case ActionCode.GetBehave when IsUnitClass(sprite):
                return (int)sprite.Behavior;
            case ActionCode.SetBehave when IsUnitClass(sprite):
                sprite.Behavior = (WeaponBehavior)var1;

                if (sprite.Link is not null)
                {
                    Action(sprite.Link, code, var1);
                }

                break;
            case ActionCode.GetBehave or ActionCode.SetBehave:
                break;
            case ActionCode.GetArmy:
                return (int)sprite.Army & ArmyMask;
            case ActionCode.SetArmy:
                ChangeArmy(sprite, var1);
                break;
            case ActionCode.SetInvisible:
                SetInvisible(sprite, var1 != 0);
                break;
            case ActionCode.GetTimer:
                return sprite.Timer;
            case ActionCode.SetTimer:
                sprite.Timer = var1;
                break;
            case ActionCode.SetZSpeed:
                sprite.VerticalSpeed = var1 * SpeedPerMillisecond;
                break;
            case ActionCode.SetSpeed:
                sprite.Speed = var1 * SpeedPerMillisecond;
                break;
            case ActionCode.GetCommand:
                return (int)sprite.Command;
            case ActionCode.SetDeathTimer:
                sprite.Lifetime = var1;
                break;
            case (ActionCode)Act.ForgottenOrder or ActionCode.SetQuest or ActionCode.WhileNotQuest or ActionCode.SetText or ActionCode.SetTextCount or
                ActionCode.SetFile or ActionCode.Select or ActionCode.ChangeCoorZ:
                break;
            default:
                throw new NotSupportedException($"Cannot send {code}.");
        }

        return 0;
    }

    [EngineRoutine(0x00440830)]
    private void ChangeVid(Sprite sprite, int id, int state)
    {
        var definition = Definitions.Find((KnownObject)id);

        if (definition is null || definition == sprite.Definition)
            return;

        if (sprite != Player)
        {
            DropLinks(sprite);
        }

        sprite.ChangeVid(definition, state < 0 ? sprite.Animation : (SpriteAnimation)state);
        Collector.ChangeCoor(sprite);

        if (sprite == Player)
        {
            Weapons.ReplaceTorso();
        }
        else
        {
            CreateLink(sprite);
        }
    }

    [EngineRoutine(0x00454800)]
    private void AddPlayerItem(Sprite man, int item)
    {
        if ((KnownObject)item is KnownObject.Dynamite or KnownObject.ExtraLife)
        {
            man.Items.Add(item);

            return;
        }

        if (!TryAddItem(man, item))
            return;

        int slot = item - Sprite.WeaponItemBase;

        if (man != Player || slot < 0 || slot >= Sprite.WeaponSlots)
            return;

        if (Player.GetAmmo() == 0 || Player.WeaponSlot < slot || slot == 0)
        {
            Weapons.SelectWeapon(slot);
        }
    }

    private static bool IsUnitClass(Sprite sprite) => sprite.Definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man;

    [EngineRoutine(0x00442ad0)]
    private void ChangeArmy(Sprite sprite, int army)
    {
        int previous = (int)sprite.Army & ArmyMask;
        sprite.Army = (Army)(army & ArmyMask);
        int current = (int)sprite.Army;

        if (sprite.Link is not null)
        {
            ChangeArmy(sprite.Link, army);
        }

        var definition = sprite.Definition;
        int newMax = definition.ArmyMaxHp[current];
        int oldMax = definition.ArmyMaxHp[previous];

        if (newMax != oldMax)
        {
            int scaled = (sprite.Hp << HpShift) / oldMax * newMax;
            ChangeHp(sprite, (scaled + (scaled >> 31 & HpRounding)) >> HpShift);
        }

        if (definition.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
        {
            SetInvisible(sprite, current != ControlledPlayer);
        }

        if (sprite.IsCounted)
        {
            if (definition.Counts[previous] != 0)
            {
                definition.Counts[previous]--;
            }

            definition.Counts[current]++;
        }
    }

    [EngineRoutine(0x00442620)]
    [EngineRoutine(0x00442640)]
    private static void SetInvisible(Sprite sprite, bool invisible)
    {
        sprite.IsInvisible = invisible;

        foreach (var link in sprite.EnumerateLinks())
        {
            link.IsInvisible = invisible;
        }
    }

    [EngineRoutine(0x00454800)]
    private void ChangePlayerVid(int id, int state)
    {
        if ((int)Player.Definition.Id < PlayerIdLimit)
        {
            Player.SlotAmmo[Player.WeaponSlot] = Player.GetAmmo();
        }

        ChangeVid(Player, id, state);

        if ((int)Player.Definition.Id > PlayerIdLimit)
        {
            Player.AddAmmo(Player.GetAmmoCapacity() - Player.GetAmmo());
        }
        else
        {
            Player.AddAmmo(Player.SlotAmmo[Player.WeaponSlot] - Player.GetAmmo());
        }
    }
}

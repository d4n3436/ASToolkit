using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class GameWorld
{
    private const int PlayerIdLimit = 20;
    private const int ControlledPlayer = 0;
    private const int HpShift = 8;
    private const int HpRounding = 0xff;
    private const float SpeedPerMillisecond = 0.001f;

    /// <summary>
    /// Sends an action to a sprite through the handler of its sprite class.
    /// </summary>
    /// <param name="unit">The sprite.</param>
    /// <param name="code">The action.</param>
    /// <param name="var1">The first argument.</param>
    /// <param name="var2">The second argument.</param>
    /// <param name="var3">The third argument.</param>
    /// <param name="target">The sprite argument.</param>
    /// <returns>The action's result.</returns>
    [EngineRoutine(0x00454800)]
    [EngineRoutine(0x0044ca70)]
    public int Action(Unit unit, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0, Unit? target = null)
    {
        switch (unit.Definition.Object.SpriteClass)
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
                        AddPlayerItem(unit, var1);

                        return 0;
                }

                break;
            case SpriteClass.Building:
                switch (code)
                {
                    case ActionCode.BuildUnit:
                        StartBuild(unit, var1, var2, var3);

                        return 0;
                    case ActionCode.NextCommand:
                        DecideBuilding(unit);

                        return 0;
                }

                break;
        }

        return UnitAction(unit, code, var1, var2, var3, target);
    }

    [EngineRoutine(0x00440830)]
    private int UnitAction(Unit unit, ActionCode code, int var1, int var2, int var3, Unit? target)
    {
        switch (code)
        {
            case ActionCode.Attack:
                MonsterBrain.Attack(unit, target);
                break;
            case ActionCode.Move:
                DrawGoalSpriteRandom();
                Motion.SetMoveTarget(unit, null, new Vector3(var1, var2, var3));
                break;
            case ActionCode.MoveTo:
                Motion.SetMoveTarget(unit, target, null);
                break;
            case ActionCode.BuildUnit:
                Build(unit, var1, var2, var3);
                break;
            case ActionCode.Stop:
                UnitKinematics.StopMoving(unit);

                if (var1 != 0)
                {
                    unit.Speed = 0;
                }

                break;
            case ActionCode.Pause:
                MonsterBrain.Pause(unit, var1 + _rng.Next() % (var2 + 1));
                break;
            case ActionCode.Turn:
                unit.SetDirection(unit.Direction.TurnToward(new Angle(unchecked((byte)var1)), unit.Definition.Object.RotationPeriod,
                    unit.Definition.Layout.GetFrameDuration(unit.Animation), out _));
                break;
            case ActionCode.ClearCommand:
                UnitKinematics.SetCommand(unit, UnitCommand.None);
                break;
            case ActionCode.FlagmanTrigger:
                if (!Player.IsAlive || Player.Position.AsVector2().MeasureOctagonalDistance(new Vector2(var1, var2)) >= var3)
                {
                    unit.Orders.Push(new UnitOrder((int)code, var1, var2, var3));
                }

                break;
            case ActionCode.ChangeVid:
                if (!unit.IsAlive && (SpriteAnimation)var2 < SpriteAnimation.Death)
                {
                    unit.Hp = unit.HpBeforeDeath;
                }

                ChangeVid(unit, var1, var2);
                break;
            case ActionCode.ChangeCoor:
                MoveSprite(unit, new Vector3(var1, var2, var3));
                break;
            case ActionCode.ChangeDirection:
                unit.SetDirection(new Angle(unchecked((byte)var1)));
                break;
            case ActionCode.ChangeAnimation:
                unit.ChangeAnimation((SpriteAnimation)var1);
                break;
            case ActionCode.GotoStack:
                unit.Orders.SetCount(var1 + 1);
                unit.Frame = unit.LastFrame;
                break;
            case ActionCode.ClearStack:
                unit.Orders.Clear();
                break;
            case ActionCode.StopStack:
                unit.Orders.Push(new UnitOrder((int)code, var1, var2, var3));
                Action(unit, ActionCode.NextCommand);
                break;
            case ActionCode.NextCommand:
                NextCommand(unit);
                break;
            case ActionCode.DestroyUnit:
                DestroySprite((KnownObject)var1, new Vector2(var2, var3));
                break;
            case ActionCode.GetAmmo:
                return unit.GetAmmo(var1);
            case ActionCode.AddAmmo:
                return unit.AddAmmo(var1, var2);
            case ActionCode.AddItem:
                unit.Items.Add(var1);
                break;
            case ActionCode.DeleteItem:
            {
                int i = unit.Items.LastIndexOf(var1);

                if (i < 0)
                    break;

                unit.Items[i] = unit.Items[^1];
                unit.Items.RemoveAt(unit.Items.Count - 1);

                return 1;
            }
            case ActionCode.HaveItem:
                return unit.Items.Contains(var1) ? 1 : 0;
            case ActionCode.DeleteAllItems:
                unit.Items.Clear();
                break;
            case ActionCode.GetItem:
                return (uint)var1 < (uint)unit.Items.Count ? unit.Items[var1] : 0;
            case ActionCode.Damage:
                Damage(unit, var1);
                break;
            case ActionCode.GetHp:
                return unit.Hp;
            case ActionCode.SetHp:
                SetUnitHp(unit, var1 != 0 ? var1 : unit.Definition.ArmyMaxHp[(int)unit.Army & ArmyMask] * var2 / Percent);
                break;
            case ActionCode.GetBehave when IsUnitClass(unit):
                return (int)unit.Behavior;
            case ActionCode.SetBehave when IsUnitClass(unit):
                unit.Behavior = (WeaponBehavior)var1;

                if (unit.Link is not null)
                {
                    Action(unit.Link, code, var1);
                }

                break;
            case ActionCode.GetBehave or ActionCode.SetBehave:
                break;
            case ActionCode.GetArmy:
                return (int)unit.Army & ArmyMask;
            case ActionCode.SetArmy:
                SetArmy(unit, var1);
                break;
            case ActionCode.SetInvisible:
                SetInvisible(unit, var1 != 0);
                break;
            case ActionCode.GetTimer:
                return unit.Timer;
            case ActionCode.SetTimer:
                unit.Timer = var1;
                break;
            case ActionCode.SetZSpeed:
                unit.VerticalSpeed = var1 * SpeedPerMillisecond;
                break;
            case ActionCode.SetSpeed:
                unit.Speed = var1 * SpeedPerMillisecond;
                break;
            case ActionCode.GetCommand:
                return (int)unit.Command;
            case ActionCode.SetDeathTimer:
                unit.Lifetime = var1;
                break;
            case (ActionCode)UnitOrder.ForgottenOrder or ActionCode.SetQuest or ActionCode.WhileNotQuest or ActionCode.SetText or ActionCode.SetTextCount or
                ActionCode.SetFile or ActionCode.Select or ActionCode.ChangeCoorZ:
                break;
            default:
                throw new NotSupportedException($"Cannot send {code}.");
        }

        return 0;
    }

    [EngineRoutine(0x00440830)]
    private void ChangeVid(Unit unit, int id, int state)
    {
        var definition = Definitions.Find((KnownObject)id);

        if (definition is null || definition == unit.Definition)
            return;

        if (unit != Player)
        {
            DropLinks(unit);
        }

        unit.ChangeVid(definition, state < 0 ? unit.Animation : (SpriteAnimation)state);
        Units.ChangeCoor(unit);

        if (unit == Player)
        {
            Weapons.ReplaceTorso();
        }
        else
        {
            CreateLink(unit);
        }
    }

    [EngineRoutine(0x00454800)]
    private void AddPlayerItem(Unit man, int item)
    {
        if ((KnownObject)item is KnownObject.Dynamite or KnownObject.ExtraLife)
        {
            man.Items.Add(item);

            return;
        }

        if (!TryAddItem(man, item))
            return;

        int slot = item - Unit.WeaponItemBase;

        if (man != Player || (uint)slot >= Unit.WeaponSlots)
            return;

        if (Player.GetAmmo() == 0 || Player.WeaponSlot < slot || slot == 0)
        {
            Weapons.SelectWeapon(slot);
        }
    }

    private static bool IsUnitClass(Unit unit) => unit.Definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man;

    [EngineRoutine(0x00442ad0)]
    private void SetArmy(Unit unit, int army)
    {
        int previous = (int)unit.Army & ArmyMask;
        unit.Army = (Army)(army & ArmyMask);
        int current = (int)unit.Army;

        if (unit.Link is not null)
        {
            SetArmy(unit.Link, army);
        }

        var definition = unit.Definition;
        int newMax = definition.ArmyMaxHp[current];
        int oldMax = definition.ArmyMaxHp[previous];

        if (newMax != oldMax)
        {
            int scaled = (unit.Hp << HpShift) / oldMax * newMax;
            SetUnitHp(unit, (scaled + (scaled >> 31 & HpRounding)) >> HpShift);
        }

        if (definition.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
        {
            SetInvisible(unit, current != ControlledPlayer);
        }

        if (unit.IsCounted)
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
    private static void SetInvisible(Unit unit, bool invisible)
    {
        for (var sprite = unit; sprite is not null; sprite = sprite.Link)
        {
            sprite.IsInvisible = invisible;
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

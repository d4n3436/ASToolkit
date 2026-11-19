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
    private const float DeepFall = -100;
    private const int IdleScanOdds = 11;
    private const int LinkScanOdds = 4;
    private const int CreaturePeriodMask = 0x7ff;
    private const int CreatureTurn = 0x20;

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
    public int Action(Sprite sprite, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0, Sprite? target = null) =>
        sprite.Definition.Object.SpriteClass switch
        {
            SpriteClass.Terrain or SpriteClass.Object => TerrainAction(sprite, code, var1, var2, var3, target),
            SpriteClass.Unit => UnitAction(sprite, code, var1, var2, var3, target),
            SpriteClass.Creature => CreatureAction(sprite, code, var1, var2, var3, target),
            SpriteClass.Cannon => CannonAction(sprite, code, var1, var2, var3, target),
            SpriteClass.Building => BuildingAction(sprite, code, var1, var2, var3, target),
            SpriteClass.Avia => AviaAction(sprite, code, var1, var2, var3, target),
            SpriteClass.Man => ManAction(sprite, code, var1, var2, var3, target),
            _ => SpriteAction(sprite, code, var1, var2, var3, target),
        };

    [EngineRoutine(0x00454800)]
    private int ManAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
    {
        switch (code)
        {
            case ActionCode.CoorAttack:
            {
                var weapon = sprite.Definition.LinkDefinition;

                if (Weapons.Torso.Definition != weapon || weapon.FightChild == 0 || weapon.Object.WeaponIndex == 0 || Weapons.Torso.IsReloading)
                    return 0;

                var point = Weapons.GetCursorPoint(new Vector2(var1, var2), weapon);
                DrawGoalSpriteRandom();
                SpriteKinematics.SetCommand(Weapons.Torso, Command.OneAttack, point: point);

                return 0;
            }
            case ActionCode.ChangeVid:
                if ((int)Player.Definition.Id < PlayerIdLimit)
                {
                    Player.SlotAmmo[Player.WeaponSlot] = Player.GetAmmo();
                }

                UnitAction(sprite, code, var1, var2, var3, target);

                if ((int)Player.Definition.Id > PlayerIdLimit)
                {
                    Player.AddAmmo(Player.GetAmmoCapacity() - Player.GetAmmo());
                }
                else
                {
                    Player.AddAmmo(Player.SlotAmmo[Player.WeaponSlot] - Player.GetAmmo());
                }

                return 0;
            case ActionCode.AddItem:
            {
                if ((KnownObject)var1 is KnownObject.Dynamite or KnownObject.ExtraLife)
                {
                    sprite.Items.Add(var1);

                    return 0;
                }

                if (!TryAddItem(sprite, var1))
                    return 0;

                int slot = var1 - Sprite.WeaponItemBase;

                if (sprite != Player || slot < 0 || slot >= Sprite.WeaponSlots)
                    return 0;

                if (Player.GetAmmo() == 0 || Player.WeaponSlot < slot || slot == 0)
                {
                    Weapons.SelectWeapon(slot);
                }

                return 0;
            }
            case ActionCode.NextCommand when sprite != Player:
                return SpriteAction(sprite, code, var1, var2, var3, target);
            case ActionCode.NextCommand:
                if (sprite.HasGoal || Weapons.Torso.HasGoal)
                {
                    Weapons.Torso.AttackTact(Math.Max(FrameTime, sprite.Definition.Layout.GetFrameDuration(sprite.Animation)));
                }

                sprite.ChangeAnimation(!sprite.Speed.IsZero() ? SpriteAnimation.Go : SpriteAnimation.Stand);

                return 0;
        }

        return UnitAction(sprite, code, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044ca70)]
    private int BuildingAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
    {
        switch (code)
        {
            case ActionCode.BuildUnit:
            {
                var definition = Definitions.Find((KnownObject)var1);

                if (sprite.Command != Command.None || definition is null)
                    return 0;

                _builds[sprite] = new PendingBuild(definition, var2, var3);
                sprite.Timer = definition.BuildTime;
                SpriteKinematics.SetCommand(sprite, Command.Build);

                if (sprite.Animation < SpriteAnimation.Death)
                {
                    sprite.ChangeAnimation(SpriteAnimation.StopMove);
                }

                return 0;
            }
            case ActionCode.NextCommand:
            {
                if (sprite.Animation >= SpriteAnimation.Death)
                    return 0;

                bool isBuilding = sprite.Command == Command.Build;

                if (isBuilding)
                {
                    sprite.ChangeAnimation(SpriteAnimation.StopMove);
                }
                else if (sprite.Animation != SpriteAnimation.StandOpen)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Stand);
                }

                if (!isBuilding || sprite.Timer != 0 || !_builds.TryGetValue(sprite, out var pending))
                    return 0;

                var position = GetBuildPosition(sprite, pending.X, pending.Y);

                if (!IsFree(pending.Definition, position))
                    return 0;

                SpriteKinematics.SetCommand(sprite, Command.None);
                var built = CreateBuilt(pending.Definition, position, sprite.Direction);

                if (built is not null)
                {
                    Action(sprite, ActionCode.CopyStackTo, target: built);
                }

                _builds.Remove(sprite);
                sprite.ChangeAnimation(SpriteAnimation.Stand);

                return 0;
            }
        }

        return UnitAction(sprite, code, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044ceb0)]
    private int AviaAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target) =>
        UnitAction(sprite, code, var1, var2, var3, target);

    [EngineRoutine(0x00448950)]
    private int CannonAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
    {
        if (code != ActionCode.NextCommand)
            return SpriteAction(sprite, code, var1, var2, var3, target);

        var state = sprite.Animation;

        if (state == SpriteAnimation.Fight)
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
        else if (state < SpriteAnimation.Death)
        {
            if (sprite.Position.Z < DeepFall)
            {
                sprite.ChangeAnimation(SpriteAnimation.Death2);
            }
            else if (!sprite.Speed.IsZero())
            {
                sprite.ChangeAnimation(SpriteAnimation.Go);
            }
            else if (state > SpriteAnimation.Open && state != SpriteAnimation.StandOpen)
            {
                sprite.ChangeAnimation(SpriteAnimation.Stand);
            }
        }

        return 0;
    }

    [EngineRoutine(0x0044d880)]
    private int CreatureAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
    {
        if (code != ActionCode.NextCommand)
            return UnitAction(sprite, code, var1, var2, var3, target);

        if (sprite.Animation >= SpriteAnimation.Death)
            return 0;

        if (sprite.Animation is SpriteAnimation.Fight or SpriteAnimation.Wound)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (sprite is { HasGoal: true, Command: Command.Move })
        {
            if (!sprite.Speed.IsZero())
            {
                if (sprite.Animation != SpriteAnimation.Go)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Go);
                }

                return 0;
            }

            sprite.ChangeAnimation(SpriteAnimation.Stand);

            return 0;
        }

        bool newPeriod = (uint)sprite.LastTickTime < (uint)(Time - (Time & CreaturePeriodMask));

        if (sprite.Command is Command.Attack or Command.OneAttack || newPeriod)
        {
            sprite.AttackTact(Math.Max(FrameTime, sprite.Definition.Layout.GetFrameDuration(sprite.Animation)));
        }

        int frameTime = (byte)sprite.Definition.Layout.GetFrameDuration(sprite.Animation);

        if (sprite.Command is not (Command.Move or Command.Attack or Command.OneAttack))
        {
            if (sprite.Animation == SpriteAnimation.LRotate && _rng.Next() % 3 != 0)
            {
                sprite.RotateTact(sprite.Direction - CreatureTurn, frameTime);
            }

            if (sprite.Animation == SpriteAnimation.RRotate && _rng.Next() % 3 != 0)
            {
                sprite.RotateTact(sprite.Direction + CreatureTurn, frameTime);
            }

            if (newPeriod)
            {
                if ((_rng.Next() & 3) == 0)
                {
                    Motion.StartMove(sprite);
                }
                else if (sprite.Speed.IsZero() && _rng.Next() % 3 == 0 && (sprite.Definition.Object.FrameCounts[(int)SpriteAnimation.Unload] != 0 || sprite.Definition.Children[(int)SpriteAnimation.Unload] != 0))
                {
                    sprite.ChangeAnimation(SpriteAnimation.Unload);
                }
                else if ((_rng.Next() & 3) == 0)
                {
                    sprite.RotateTact(sprite.Direction + ((_rng.Next() & 1) == 0 ? CreatureTurn : -CreatureTurn), frameTime);
                }
                else
                {
                    SpriteKinematics.Stop(sprite);
                }
            }
        }

        if (sprite.Animation is SpriteAnimation.Wound or SpriteAnimation.Salut)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }


        return 0;
    }

    [EngineRoutine(0x00447500)]
    private int UnitAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
    {
        switch (code)
        {
            case ActionCode.NextCommand:
            {
                if (sprite.Animation >= SpriteAnimation.Death)
                    return 0;

                if (sprite.Speed.IsZero())
                {
                    sprite.ChangeAnimation(SpriteAnimation.Stand);
                }
                else if (sprite.Animation != SpriteAnimation.Go)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Go);
                }

                var definition = sprite.Definition;
                var link = sprite.ArmedLink;
                var result = AttackResult.Pursue;

                if (link is not null && sprite.Command is Command.Attack or Command.OneAttack && sprite.Goal is not null && link.Goal is null)
                {
                    SpriteKinematics.SetCommand(link, sprite.Command, sprite.Goal);
                }

                if (link is not null || sprite.TurnTicks == 0)
                {
                    result = sprite.AttackTact(Math.Max(FrameTime, definition.Layout.GetFrameDuration(sprite.Animation)));

                    switch (result)
                    {
                        case AttackResult.NoAmmo or AttackResult.GiveUp:
                            SpriteKinematics.SetCommand(sprite, Command.None);
                            break;
                        case AttackResult.Approach when definition.Speed.IsZero():
                            if (link is not null && link.HasGoal || sprite.HasGoal)
                            {
                                SpriteKinematics.SetCommand(sprite, Command.None);
                            }
                            break;
                        case AttackResult.Approach when sprite.Speed.IsZero():
                            Motion.StartMove(sprite);
                            break;
                        case AttackResult.Attacked when !sprite.Speed.IsZero() && (link is null || sprite.Goal == link.Goal):
                            SpriteKinematics.Stop(sprite);
                            break;
                    }

                    if (result == AttackResult.Approach && link is not null && sprite.Command is Command.Attack or Command.OneAttack &&
                        sprite.IsAggressive && (link.Timer != 0 || _rng.Next() % LinkScanOdds == 0))
                    {
                        var enemy = _brain.SeekEnemy(sprite, Player);

                        if (enemy is not null)
                        {
                            SpriteKinematics.SetCommand(link, Command.OneAttack, enemy);
                        }
                    }
                }

                if (result == AttackResult.Pursue && sprite.IsActive && sprite.Speed.IsZero())
                {
                    Motion.StartMove(sprite);
                }

                if (result == AttackResult.NoOrder && sprite is { IsAggressive: true, IsActive: true } ||
                    result is AttackResult.Pursue or AttackResult.NoTarget && sprite is { IsAggressive: true, Command: Command.None or Command.Move or Command.OneAttack } &&
                    (sprite.LastFrame > sprite.FirstFrame || sprite.Timer != 0 || _rng.Next(IdleScanOdds) == 0))
                {
                    var enemy = _brain.SeekEnemy(sprite, Player);

                    if (enemy is not null)
                    {
                        SpriteKinematics.SetCommand(sprite, Command.OneAttack, enemy);
                    }
                }

                if (sprite is { Command: Command.None, HasGoal: false, Timer: 0 })
                {
                    SpriteKinematics.Stop(sprite);
                }

                return 0;
            }
            case ActionCode.GetBehave when IsUnitClass(sprite):
                return (int)sprite.Behavior;
            case ActionCode.SetBehave when IsUnitClass(sprite):
                sprite.Behavior = (WeaponBehavior)var1;

                if (sprite.Link is not null)
                {
                    Action(sprite.Link, code, var1);
                }

                return 0;
        }

        return TerrainAction(sprite, code, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044bea0)]
    private int TerrainAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target) =>
        SpriteAction(sprite, code, var1, var2, var3, target);

    [EngineRoutine(0x00440830)]
    private int SpriteAction(Sprite sprite, ActionCode code, int var1, int var2, int var3, Sprite? target)
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
            {
                var definition = Definitions.Find((KnownObject)var1);

                if (definition is null)
                    break;

                var built = CreateBuilt(definition, GetBuildPosition(sprite, var2, var3), sprite.Direction);

                if (built is not null)
                {
                    Action(sprite, ActionCode.CopyStackTo, target: built);
                }

                break;
            }
            case ActionCode.Stop:
                SpriteKinematics.Stop(sprite);

                if (var1 != 0)
                {
                    sprite.Speed = 0;
                }

                break;
            case ActionCode.Pause:
            {
                if (!sprite.Speed.IsZero())
                {
                    SpriteKinematics.Stop(sprite);
                }

                int time = var1 + _rng.Next() % (var2 + 1);
                sprite.Timer = time;
                SpriteKinematics.SetCommand(sprite, time != 0 ? Command.Pause : Command.None);
                break;
            }
            case ActionCode.Turn:
                sprite.RotateTact(new Angle(unchecked((byte)var1)), sprite.Definition.Layout.GetFrameDuration(sprite.Animation));
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

            {
                var definition = Definitions.Find((KnownObject)var1);

                if (definition is null || definition == sprite.Definition)
                    break;

                if (sprite != Player)
                {
                    DropLinks(sprite);
                }

                sprite.ChangeVid(definition, var2 < 0 ? sprite.Animation : (SpriteAnimation)var2);
                Collector.ChangeCoor(sprite);

                if (sprite == Player)
                {
                    Weapons.ReplaceTorso();
                }
                else
                {
                    CreateLink(sprite);
                }

                break;
            }
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
            case ActionCode.CopyStackTo:
                if (target is null || target == sprite)
                    break;

                target.ActionStack.Clear();
                foreach (var order in sprite.ActionStack.Orders)
                {
                    if (order.Value == Act.StopMarker)
                        break;

                    target.ActionStack.Push(order);
                }

                break;
            case ActionCode.StopStack:
                sprite.ActionStack.Push(new Act((int)code, var1, var2, var3));
                Action(sprite, ActionCode.NextCommand);
                break;
            case ActionCode.NextCommand:
            {
                var state = sprite.Animation;

                if (state >= SpriteAnimation.Death)
                    break;

                if (sprite.Speed.IsZero())
                {
                    if (state > SpriteAnimation.RRotate && state != SpriteAnimation.StandOpen)
                    {
                        sprite.ChangeAnimation(SpriteAnimation.Stand);
                    }
                }
                else if (state != SpriteAnimation.Go)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Go);
                }

                break;
            }
            case ActionCode.DestroyUnit:
            {
                var placement = FindSprite((KnownObject)var1, new Vector2(var2, var3));

                if (placement is not null)
                {
                    Destroy(placement);
                }

                break;
            }
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
            case ActionCode.GetBehave or ActionCode.SetBehave:
                break;
            case ActionCode.GetArmy:
                return (int)sprite.Army & ArmyMask;
            case ActionCode.SetArmy:
                ChangeArmy(sprite, var1);
                break;
            case ActionCode.SetInvisible:
                sprite.IsInvisible = var1 != 0;

                if (var1 == 0)
                {
                    sprite.Link?.InvisibleOff();
                }
                else
                {
                    sprite.Link?.InvisibleOn();
                }
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
            sprite.IsInvisible = current != ControlledPlayer;

            if (current == ControlledPlayer)
            {
                sprite.Link?.InvisibleOff();
            }
            else
            {
                sprite.Link?.InvisibleOn();
            }
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

}

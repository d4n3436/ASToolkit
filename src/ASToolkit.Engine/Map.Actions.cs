using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private const int PlayerIdLimit = 20;
    private const int WornArmorItemBase = 204;
    private const uint ReloadTimerLimit = 5000;
    private const int ControlledPlayer = 0;
    private const int HpShift = 8;
    private const int HpRounding = 0xff;
    private const float SpeedPerMillisecond = 0.001f;
    private const float ScriptSpeedScale = 1000;
    private const float UnitAimLift = 80;
    private const float GroundAimLift = 19;
    private const int QuarterTurn = Angle.QuarterTurn;
    private const float DeepFall = -100;
    private const int IdleScanOdds = 11;
    private const int LinkScanOdds = 4;
    private const int CreaturePeriodMask = 0x7ff;
    private const int CreatureTurn = 0x20;
    private const float WanderReach = 64;
    private const float CreatureAlertReach = 150;
    private const KnownObject RelinkCountedId = (KnownObject)35;
    private const KnownObject RelinkLimitId = KnownObject.Slug;
    private const KnownObject RelinkEffectId = (KnownObject)590;
    private const OldSpriteType BuiltSpriteType = OldSpriteType.Unit;
    private const int NoItem = -1;

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
    public int Action(Sprite sprite, ActionCode code, int var1 = 0, int var2 = 0, int var3 = 0, Sprite? target = null) =>
        sprite.Action(this, (int)code, var1, var2, var3, target);

    [EngineRoutine(0x00454800)]
    internal int ManAction(Man sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.CoorAttack:
                ManCoorAttack(sprite, new Vector2(var1, var2));
                return 0;
            case ActionCode.ChangeVid:
                ManChangeVid(sprite, var1, var2, var3, target);
                return 0;
            case ActionCode.AddItem:
                ManAddItem(sprite, var1);
                return 0;
            case ActionCode.Damage:
                return ManDamage(sprite, var1, var2, var3, target);
            case ActionCode.GetAmmo:
                if (var1 != 0 && var1 != sprite.WeaponSlot)
                    return (uint)var1 < (uint)sprite.SlotAmmo.Length ? sprite.SlotAmmo[var1] : 0;

                return UnitAction(sprite, act, 0, 0, 0, null);
            case ActionCode.AddAmmo:
                // TODO: Port AS1's write before the slot array for a negative slot; the port returns 0
                if ((uint)var2 >= (uint)sprite.SlotAmmo.Length)
                    return 0;

                if (var2 != 0 && var2 != sprite.WeaponSlot)
                    return sprite.SlotAmmo[var2] += var1;

                return UnitAction(sprite, act, var1, 0, 0, null);
            case ActionCode.NextCommand:
                if (sprite.Animation >= SpriteAnimation.Death)
                    return 0;

                if (sprite.HasGoal || sprite.Link is { HasGoal: true })
                {
                    sprite.AttackResult = sprite.AttackTact(Math.Max(FrameTime, sprite.Definition.GetVisualFrameDuration(sprite.Animation)));
                }

                sprite.ChangeAnimation(!sprite.Speed.IsExactlyZero() ? SpriteAnimation.Go : SpriteAnimation.Stand);

                return 0;
        }

        return UnitAction(sprite, act, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044ca70)]
    internal int BuildingAction(Building sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        bool noItem;
        SpriteDefinition? definition;
        bool isBuilding;
        Vector3 position;
        switch ((ActionCode)act)
        {
            case ActionCode.BackupCommand when sprite.Command == Command.Build && _builds.TryGetValue(sprite, out var building):
                sprite.ActionStack.Push(new Act((int)ActionCode.BuildUnit, (int)building.Definition.Id, 0, 0));
                SpriteKinematics.SetCommand(sprite, Command.None);

                return 0;
            case ActionCode.BuildUnit:
                noItem = false;

                if (var1 == 0)
                {
                    var1 = Action(sprite, ActionCode.GetItemType, (int)BuiltSpriteType);
                    noItem = var1 == 0;
                }

                definition = var1 >= 0 ? Definitions.Find((KnownObject)var1) : null;

                if (!(noItem || sprite.Command == Command.None) || definition is null)
                    return 0;

                _builds[sprite] = new PendingBuild(definition, new Point(var2, var3));
                sprite.Timer = definition.BuildTime;
                SpriteKinematics.SetCommand(sprite, Command.Build);

                if (sprite.Animation < SpriteAnimation.Death)
                {
                    sprite.ChangeAnimation(SpriteAnimation.StopMove);
                }

                return 0;
            case ActionCode.NextCommand:
                if (sprite.Animation >= SpriteAnimation.Death)
                    return 0;

                isBuilding = sprite.Command == Command.Build;

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

                position = GetBuildPosition(sprite, pending.Offset);

                if (!IsFree(pending.Definition, position))
                    return 0;

                SpriteKinematics.SetCommand(sprite, Command.None);
                AddBuiltUnit(sprite, pending.Definition, position);
                _builds.Remove(sprite);
                sprite.ChangeAnimation(SpriteAnimation.Stand);

                return 0;
        }

        return UnitAction(sprite, act, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044ceb0)]
    internal int AviaAction(Avia sprite, int act, int var1, int var2, int var3, Sprite? target) =>
        UnitAction(sprite, act, var1, var2, var3, target);

    [EngineRoutine(0x00448950)]
    internal int CannonAction(Cannon sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        if ((ActionCode)act != ActionCode.NextCommand)
            return SpriteAction(sprite, act, var1, var2, var3, target);

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
            else if (!sprite.Speed.IsExactlyZero())
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
    internal int CreatureAction(Creature sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        if (code == ActionCode.Damage && var1 > 0)
        {
            AlertCreatures(sprite);
        }

        if (code != ActionCode.NextCommand)
            return UnitAction(sprite, act, var1, var2, var3, target);

        if (sprite.Animation >= SpriteAnimation.Death)
            return 0;

        if (sprite.Animation is SpriteAnimation.Fight or SpriteAnimation.Wound)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (sprite is { IsSeekingRegion: true, Region: null })
        {
            sprite.IsSeekingRegion = false;
            sprite.Region = FindRegion(sprite.Position.AsVector2());
        }

        if (sprite is { HasGoal: true, Command: Command.Move })
        {
            if (!sprite.Speed.IsExactlyZero())
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
            TactCreatureAttack(sprite);
        }

        int frameTime = (byte)sprite.Definition.GetVisualFrameDuration(sprite.Animation);

        if (sprite.Command is not (Command.Move or Command.Attack or Command.OneAttack))
        {
            WanderCreature(sprite, frameTime, newPeriod);
        }

        if (sprite.Animation is SpriteAnimation.Wound or SpriteAnimation.Salut)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        return 0;
    }

    [EngineRoutine(0x00447500)]
    internal int UnitAction(Unit sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        switch (code)
        {
            case ActionCode.Damage:
                return UnitDamage(sprite, var1, var2, var3, target);
            case ActionCode.NextCommand:
                UnitNextCommand(sprite);
                return 0;
            case ActionCode.Repair:
                sprite.FillAmmo();

                return TerrainAction(sprite, act, var1, var2, var3, target);
            case ActionCode.GetAmmo:
                return sprite.GetAmmo();
            case ActionCode.AddAmmo:
                return sprite.AddAmmo(var1);
            case ActionCode.GetBehave:
                return (int)sprite.Behavior;
            case ActionCode.SetBehave:
                sprite.Behavior = (WeaponBehavior)var1;

                if (sprite.Link is not null)
                {
                    Action(sprite.Link, code, var1);
                }

                return 0;
        }

        return TerrainAction(sprite, act, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044bea0)]
    internal int TerrainAction(Terrain sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        if ((ActionCode)act != ActionCode.Repair)
            return SpriteAction(sprite, act, var1, var2, var3, target);

        Repair(sprite, true);

        return 0;
    }

    [EngineRoutine(0x00440830)]
    internal int SpriteAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)(act & byte.MaxValue);
        switch (code)
        {
            case ActionCode.GetAmmo or ActionCode.AddAmmo:
                return sprite.Owner is null ? 0 : sprite.Owner.Action(this, act, var1, var2, var3, target);
            case ActionCode.Salut:
                if (sprite.Animation != SpriteAnimation.Fight)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Salut);
                }

                break;
            case ActionCode.Death:
                if (sprite.Definition.Damage != 0)
                {
                    sprite.Hp = 0;
                    Weapons.ApplyBlast(sprite);
                }

                break;
            case ActionCode.CoorAttack:
                SpriteCoorAttack(sprite, new Point(var1, var2));
                break;
            case ActionCode.Random:
                SpriteRandom(sprite);
                break;
            case ActionCode.GetGoal:
                return GetSpriteHandle(sprite.Goal);
            case ActionCode.SetGoalCoor:
                sprite.Goal = ConstructGoal(new Vector3(var1, var2, var3));
                break;
            case ActionCode.GetBattleRange:
                return SpriteGetBattleRange(sprite);
            case ActionCode.GetLink:
                return GetSpriteHandle(sprite.Link);
            case ActionCode.GetUplink:
                return GetSpriteHandle(sprite.Owner);
            case ActionCode.GetZSpeed:
                return (int)(sprite.VerticalSpeed * ScriptSpeedScale);
            case ActionCode.GetSpeed:
                return (int)(sprite.Speed * ScriptSpeedScale);
            case ActionCode.Attack:
                Attack(sprite, target);
                break;
            case ActionCode.Move:
                Motion.SetMoveTarget(sprite, ConstructGoal(new Vector3(var1, var2, var3)));
                break;
            case ActionCode.MoveTo:
                Motion.SetMoveTarget(sprite, target);
                break;
            case ActionCode.BuildUnit:
                SpriteBuildUnit(sprite, var1, new Point(var2, var3));
                break;
            case ActionCode.PlaySfx:
                PlaySfx(sprite, var1);
                break;
            case ActionCode.Stop:
                SpriteKinematics.Stop(sprite);

                if (var1 != 0)
                {
                    sprite.Speed = 0;
                }

                break;
            case ActionCode.Pause:
                SpritePause(sprite, var1, var2);
                break;
            case ActionCode.Turn:
                sprite.RotateTact(new Angle(unchecked((byte)var1)), sprite.Definition.GetVisualFrameDuration(sprite.Animation));
                break;
            case ActionCode.ClearCommand:
                SpriteKinematics.SetCommand(sprite, Command.None);
                break;
            case ActionCode.FlagmanTrigger:
                SpriteFlagmanTrigger(sprite, new Point(var1, var2), var3);
                break;
            case ActionCode.ChangeVid:
                SpriteChangeVid(sprite, var1, var2);
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
            case ActionCode.BackupCommand:
                sprite.ActionStack.Push(new Act((int)sprite.Command << 8 | (int)ActionCode.RestoreCommand, GetSpriteHandle(sprite.Goal), 0, 0, sprite.Goal));
                sprite.Goal?.AddRef();
                break;
            case ActionCode.RestoreCommand:
                SpriteKinematics.SetCommand(sprite, (Command)(act >> 8 & byte.MaxValue), target);

                if (target is not null)
                {
                    Release(target);
                }

                break;
            case ActionCode.ClearStack:
                sprite.ActionStack.Clear();
                break;
            case ActionCode.GetStackCount:
                return sprite.ActionStack.Count;
            case ActionCode.GetPercentHp:
                return sprite.GetPercentHp() * 100 / 0xff;
            case ActionCode.CopyStackTo:
                SpriteCopyStackTo(sprite, target);
                break;
            case ActionCode.StopStack:
                sprite.ActionStack.Push(new Act((int)code, var1, var2, var3));
                Action(sprite, ActionCode.NextCommand);
                break;
            case ActionCode.NextCommand:
                SpriteNextCommand(sprite);
                break;
            case ActionCode.DestroyUnit:
                SpriteDestroyUnit(var1, new Vector2(var2, var3));
                break;
            case ActionCode.AddItem:
                sprite.InsertItem(var1);
                break;
            case ActionCode.DeleteItem:
                if (sprite.RemoveItemAt(sprite.GetItemNumber(var1)))
                    return 1;

                break;
            case ActionCode.HaveItem:
                return sprite.GetItemNumber(var1) >= 0 ? 1 : 0;
            case ActionCode.DeleteAllItems:
                sprite.Items.Clear();
                break;
            case ActionCode.GetItem:
                return var1 >= 0 && var1 < sprite.Items.Count ? sprite.Items[var1] : 0;
            case ActionCode.GetItemType:
                return SpriteGetItemType(sprite, var1);
            case ActionCode.Damage:
                return SpriteDamage(sprite, var1, var2);
            case ActionCode.Repair:
                SpriteRepair(sprite);
                break;
            case ActionCode.GetHp:
                return sprite.Hp;
            case ActionCode.SetHp:
                ChangeHp(sprite, var1 != 0 ? var1 : sprite.Definition.GetArmyMaxHp((int)sprite.Army) * var2 / Percent);
                break;
            case ActionCode.SetBehave:
                break;
            case ActionCode.GetArmy:
                return (int)sprite.Army & SpriteDefinition.ArmyMask;
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
                Session.Logger.Error($"SPRITE {(int)sprite.Definition.Id}", LogError.Text, 10);
                break;
        }

        return 0;
    }

    private static void SetInvisible(Sprite sprite, bool isInvisible)
    {
        sprite.IsInvisible = isInvisible;

        if (isInvisible)
        {
            sprite.Link?.InvisibleOn();
        }
        else
        {
            sprite.Link?.InvisibleOff();
        }
    }

    private static int SpriteGetBattleRange(Sprite sprite)
    {
        var armedLink = sprite.ArmedLink;

        return (int)(sprite.Definition.BattleRange.IsExactlyZero() && armedLink is not null ? armedLink.Definition.BattleRange : sprite.Definition.BattleRange);
    }

    private static void SpriteNextCommand(Sprite sprite)
    {
        var state = sprite.Animation;

        if (state >= SpriteAnimation.Death)
            return;

        if (sprite.Speed.IsExactlyZero())
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
    }

    [EngineRoutine(0x0044d7b0)]
    private Region? FindRegion(Vector2 point)
    {
        var list = _updates.GetLayer(ObjectLayer.Region);

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is not Region sprite || !sprite.Definition.Object.SpriteType.ToOldSpriteType().HasFlag(OldSpriteType.Region) || sprite.Definition.Class != SpriteClass.Region)
                continue;

            float halfWidth = sprite.Size.X * 0.5f;

            if (sprite.Position.X - halfWidth > point.X || point.X > halfWidth + sprite.Position.X)
                continue;

            float halfHeight = sprite.Size.Y * 0.5f;

            if (sprite.Position.Y - halfHeight <= point.Y && point.Y <= halfHeight + sprite.Position.Y)
                return sprite;
        }

        return null;
    }

    [EngineRoutine(0x0044bf10)]
    private void Repair(Sprite sprite, bool relink)
    {
        int army = (int)sprite.Army & SpriteDefinition.ArmyMask;
        ChangeHp(sprite, sprite.Definition.ArmyMaxHp[army]);
        var linkDefinition = sprite.Definition.LinkDefinition;

        if (linkDefinition is not null && sprite.Link?.Definition != linkDefinition && relink)
        {
            var counted = Definitions.Find(RelinkCountedId);
            var limit = Definitions.Find(RelinkLimitId);
            bool atLimit = sprite.Definition.Id == RelinkCountedId && (counted?.Counts[army] ?? 0) <= (limit?.Counts[army] ?? 0);

            if (!atLimit)
            {
                CreateLink(sprite);
                AddSprite(LookupDefinition((int)RelinkEffectId), sprite.Position, default);

                return;
            }
        }

        if (sprite.Link is not null && sprite.Link.Definition == linkDefinition)
        {
            Action(sprite.Link, ActionCode.Repair);
        }
    }

    [EngineRoutine(0x00445fb0)]
    private void Attack(Sprite sprite, Sprite? target)
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

    private void ManCoorAttack(Man sprite, Vector2 point)
    {
        var weapon = sprite.Definition.LinkDefinition;
        var torso = sprite.Link;

        if (torso is null || torso.Definition != weapon || weapon.FightChild == 0 || weapon.Object.WeaponIndex == 0 || (uint)torso.Timer > ReloadTimerLimit ||
            torso.Animation == SpriteAnimation.Fight && torso.Frame <= torso.LastFrame)
            return;

        var goal = ConstructGoal(Weapons.GetCursorPoint(sprite, point, weapon));
        SpriteKinematics.SetCommand(torso, Command.OneAttack, goal);
    }

    private void ManChangeVid(Man sprite, int var1, int var2, int var3, Sprite? target)
    {
        if ((int)sprite.Definition.Id < PlayerIdLimit)
        {
            sprite.SlotAmmo[sprite.WeaponSlot] = UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null);
        }

        UnitAction(sprite, (int)ActionCode.ChangeVid, var1, var2, var3, target);

        if ((int)sprite.Definition.Id > PlayerIdLimit)
        {
            var armed = sprite.Definition.LinkDefinition;
            int capacity = armed is not null && armed.FightChild != 0 && armed.Object.WeaponIndex != 0 ? armed.AmmoCapacity : sprite.Definition.AmmoCapacity;
            UnitAction(sprite, (int)ActionCode.AddAmmo, capacity - UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null), 0, 0, null);

            return;
        }

        int armorItem = sprite.GetItemNumber(WornArmorItemBase) >= 0 ? WornArmorItemBase
            : sprite.GetItemNumber(WornArmorItemBase + 1) >= 0 ? WornArmorItemBase + 1
            : sprite.GetItemNumber(WornArmorItemBase + 2) >= 0 ? WornArmorItemBase + 2
            : 0;

        if (armorItem != 0)
        {
            var position = new Vector3(sprite.Position.X, sprite.Position.Y, sprite.Position.Z + sprite.Definition.Object.LinkOffset.Z);
            var armor = AddSprite(Definitions.Find((KnownObject)(armorItem - WornArmorItemBase + (int)KnownObject.WornGreenArmor))!, position, sprite.Direction);
            AddLink(sprite.Link!, armor);
        }

        UnitAction(sprite, (int)ActionCode.AddAmmo, sprite.SlotAmmo[sprite.WeaponSlot] - UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null), 0, 0, null);
    }

    private void ManAddItem(Man sprite, int item)
    {
        if ((KnownObject)item is KnownObject.Dynamite or KnownObject.ExtraLife)
        {
            sprite.InsertItem(item);

            return;
        }

        if (!sprite.InsertUniqueItem(item))
            return;

        int slot = item - Sprite.WeaponItemBase;

        if (slot is < 0 or >= Sprite.WeaponSlots)
            return;

        if (UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null) == 0 || sprite.WeaponSlot < slot || slot == 0)
        {
            Weapons.ChangeWeapon(sprite, slot);
        }
    }

    private int ManDamage(Man sprite, int damage, int var2, int var3, Sprite? target)
    {
        if (damage > 0)
        {
            if (sprite.Definition.Id != KnownObject.MannedCannon)
            {
                foreach (var link in sprite.EnumerateLinks())
                {
                    if (link.Definition.Id is KnownObject.InvulnerabilityEffect or KnownObject.RescueMedkitEffect)
                        return 0;
                }

                foreach (var link in sprite.EnumerateLinks())
                {
                    int armor = link.Definition.Id - KnownObject.WornGreenArmor;

                    if (armor is < 0 or >= WornArmorShareCount)
                        continue;

                    int share = WornArmorShares[armor];
                    Action(link, ActionCode.Damage, (share * damage + ArmorRounding) / Percent, var2, var3);
                    damage -= share * damage / Percent;
                }
            }

            if (damage >= sprite.Hp && Action(sprite, ActionCode.HaveItem, (int)KnownObject.Medkit) != 0)
            {
                Action(sprite, ActionCode.DeleteItem, (int)KnownObject.Medkit);
                ChangeHp(sprite, sprite.Definition.GetArmyMaxHp((int)sprite.Army));
                AddSprite(Definitions.Find(KnownObject.RescueMedkitEffect)!, sprite.Position + Vector3.UnitZ * RescueEffectHeight, default, sprite);

                return 0;
            }
        }

        return SpriteAction(sprite, (int)ActionCode.Damage, damage, var2, var3, target);
    }

    private void AlertCreatures(Creature sprite)
    {
        var center = sprite.Position.AsVector2();
        var reach = new Vector2(CreatureAlertReach);
        for (var other = Collector.First(center - reach, center + reach); other is not null; other = Collector.NextHashInBox())
        {
            if (other.Definition.Class == SpriteClass.Creature && center.MeasureOctagonalDistance(other.Position.AsVector2()) < CreatureAlertReach)
            {
                Motion.StartMove(other);
            }
        }
    }

    private void TactCreatureAttack(Creature sprite)
    {
        var result = sprite.AttackTact(Math.Max(FrameTime, sprite.Definition.GetVisualFrameDuration(sprite.Animation)));
        sprite.AttackResult = result;

        if (result == AttackResult.Approach && sprite.Speed.IsExactlyZero() && (sprite.ArmedLink is { HasGoal: true } || sprite.HasGoal))
        {
            SpriteKinematics.SetCommand(sprite, Command.None);
        }

        if (result is AttackResult.Pursue or AttackResult.NoTarget && sprite.IsAggressive && sprite.Command is Command.None or Command.Move)
        {
            AttackSeenEnemy(sprite, sprite);
        }
    }

    private void WanderCreature(Creature sprite, int frameTime, bool newPeriod)
    {
        if (sprite.Animation == SpriteAnimation.LRotate && _rng.Next() % 3 != 0)
        {
            sprite.RotateTact(sprite.Direction - CreatureTurn, frameTime);
        }

        if (sprite.Animation == SpriteAnimation.RRotate && _rng.Next() % 3 != 0)
        {
            sprite.RotateTact(sprite.Direction + CreatureTurn, frameTime);
        }

        if (!newPeriod)
            return;

        if ((_rng.Next() & 3) == 0)
        {
            Motion.StartMove(sprite);
        }
        else if (sprite.Speed.IsExactlyZero() && _rng.Next() % 3 == 0 && (sprite.Definition.Object.FrameCounts[(int)SpriteAnimation.Unload] != 0 || sprite.Definition.Children[(int)SpriteAnimation.Unload] != 0 || sprite.Definition.Sounds[(int)SpriteAnimation.Unload] != 0))
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

    private void AttackSeenEnemy(Sprite seeker, Sprite attacker)
    {
        var enemy = Brain.SeekEnemy(seeker, GetFlagman(0));

        if (enemy is not null)
        {
            SpriteKinematics.SetCommand(attacker, Command.OneAttack, enemy);
        }
    }

    private int UnitDamage(Unit sprite, int damage, int var2, int var3, Sprite? target)
    {
        int result = TerrainAction(sprite, (int)ActionCode.Damage, damage, var2, var3, target);

        if (damage >= 0 && sprite.Command == Command.None && !sprite.Definition.Speed.IsExactlyZero())
        {
            var wander = sprite.Position.AsVector2() + new Angle((byte)_rng.Next()).Vector * WanderReach;
            Brain.MoveTo(sprite, ConstructGoal(new Vector3(wander, sprite.Position.Z)));
        }

        return result;
    }

    private void UnitNextCommand(Unit sprite)
    {
        if (sprite.Animation >= SpriteAnimation.Death)
            return;

        if (sprite.Speed.IsExactlyZero())
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }
        else if (sprite.Animation != SpriteAnimation.Go)
        {
            sprite.ChangeAnimation(SpriteAnimation.Go);
        }

        var link = sprite.ArmedLink;
        var result = AttackResult.Pursue;

        if (link is not null && sprite.Command is Command.Attack or Command.OneAttack && sprite.Goal is not null && link.Goal is null)
        {
            SpriteKinematics.SetCommand(link, sprite.Command, sprite.Goal);
        }

        if (link is not null || sprite.TurnTicks == 0)
        {
            result = TactUnitAttack(sprite, link);
        }

        if (result == AttackResult.Pursue && sprite.IsActive && sprite.Speed.IsExactlyZero())
        {
            Motion.StartMove(sprite);
        }

        if (result == AttackResult.NoOrder && sprite is { IsAggressive: true, IsActive: true } ||
            result is AttackResult.Pursue or AttackResult.NoTarget && sprite is { IsAggressive: true, Command: Command.None or Command.Move or Command.OneAttack } &&
            (sprite.LastFrame > sprite.FirstFrame || sprite.Timer != 0 || _rng.Next(IdleScanOdds) == 0))
        {
            AttackSeenEnemy(sprite, sprite);
        }

        if (sprite is { Command: Command.None, HasGoal: false, Timer: 0 })
        {
            SpriteKinematics.Stop(sprite);
        }
    }

    private AttackResult TactUnitAttack(Unit sprite, Sprite? link)
    {
        var definition = sprite.Definition;
        var result = sprite.AttackTact(Math.Max(FrameTime, definition.GetVisualFrameDuration(sprite.Animation)));

        switch (result)
        {
            case AttackResult.NoAmmo or AttackResult.GiveUp:
                SpriteKinematics.SetCommand(sprite, Command.None);
                break;
            case AttackResult.Approach when definition.Speed.IsExactlyZero():
                if (link is not null && link.HasGoal || sprite.HasGoal)
                {
                    SpriteKinematics.SetCommand(sprite, Command.None);
                }
                break;
            case AttackResult.Approach when sprite.Speed.IsExactlyZero():
                Motion.StartMove(sprite);
                break;
            case AttackResult.Attacked when !sprite.Speed.IsExactlyZero() && (link is null || sprite.Goal == link.Goal):
                SpriteKinematics.Stop(sprite);
                break;
        }

        if (result == AttackResult.Approach && link is not null && sprite.Command is Command.Attack or Command.OneAttack &&
            sprite.IsAggressive && (link.Timer != 0 || _rng.Next() % LinkScanOdds == 0))
        {
            AttackSeenEnemy(sprite, link);
        }

        return result;
    }

    private void SpriteCoorAttack(Sprite sprite, Point point)
    {
        var armed = (sprite.ArmedLink ?? sprite).Definition;
        int y = point.Y;
        float z;

        if (armed.Targets == SpriteType.Unit)
        {
            z = Movement.GetHeight(new Vector2(point.X, point.Y + UnitAimLift)) + UnitAimLift;
            y += (int)z;
        }
        else
        {
            z = Movement.GetHeight(new Vector2(point.X, point.Y)) + GroundAimLift;
            y += (int)z - (int)GroundAimLift;
        }

        SpriteKinematics.SetCommand(sprite, Command.OneAttack, ConstructGoal(new Vector3(point.X, y, z)));
    }

    private void SpriteRandom(Sprite sprite)
    {
        int duration = (byte)sprite.Definition.GetVisualFrameDuration(sprite.Animation);

        if (_rng.Next() % 5 == 0)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }
        else if (_rng.Next() % 5 == 0)
        {
            sprite.ChangeAnimation(SpriteAnimation.Unload);
        }
        else if (sprite.Animation == SpriteAnimation.LRotate && _rng.Next() % 3 != 0)
        {
            sprite.RotateTact(sprite.Direction - QuarterTurn, duration);
        }
        else if (sprite.Animation == SpriteAnimation.RRotate && _rng.Next() % 3 != 0)
        {
            sprite.RotateTact(sprite.Direction + QuarterTurn, duration);
        }
        else if (_rng.Next() % 4 == 0)
        {
            sprite.RotateTact(_rng.Next() % 2 == 0 ? sprite.Direction + QuarterTurn : sprite.Direction - QuarterTurn, duration);
        }
    }

    private void SpriteBuildUnit(Sprite sprite, int id, Point offset)
    {
        if (id == 0)
        {
            id = Action(sprite, ActionCode.GetItemType, (int)BuiltSpriteType);
        }

        if (id <= 0)
            return;

        var definition = Definitions.Find((KnownObject)id);

        if (definition is null)
            return;

        AddBuiltUnit(sprite, definition, GetBuildPosition(sprite, offset));
    }

    private void AddBuiltUnit(Sprite builder, SpriteDefinition definition, Vector3 position)
    {
        var built = AddSprite(definition, position, builder.Direction);

        if (built is not null)
        {
            Action(builder, ActionCode.CopyStackTo, target: built);
        }
    }

    private void SpritePause(Sprite sprite, int time, int spread)
    {
        if (!sprite.Speed.IsExactlyZero())
        {
            SpriteKinematics.Stop(sprite);
        }

        int pause = time + _rng.Next() % (spread + 1);
        sprite.Timer = pause;
        SpriteKinematics.SetCommand(sprite, pause != 0 ? Command.Pause : Command.None);
    }

    private void SpriteFlagmanTrigger(Sprite sprite, Point point, int range)
    {
        var flagman = GetFlagman(ControlledPlayer);

        if (flagman is null || flagman.Position.AsVector2().MeasureOctagonalDistance(new Vector2(point.X, point.Y)) >= range)
        {
            sprite.ActionStack.Push(new Act((int)ActionCode.FlagmanTrigger, point.X, point.Y, range));
        }
    }

    private void SpriteChangeVid(Sprite sprite, int id, int animation)
    {
        var definition = Definitions.Find((KnownObject)id);

        if (definition is null || definition == sprite.Definition)
            return;

        DropLinks(sprite);
        Collector.DeleteFromHash(sprite);
        RemoveFromUpdates(sprite);
        var state = animation < 0 ? sprite.Animation : (SpriteAnimation)animation;
        var direction = sprite.Direction;
        sprite.ChangeVid(definition);
        AddToUpdates(sprite);
        Collector.InsertToHash(sprite);

        CreateLink(sprite);

        sprite.ChangeAnimation(state);
        sprite.ChangeDirection(direction);
    }

    private void SpriteCopyStackTo(Sprite sprite, Sprite? target)
    {
        if (target is null || target == sprite)
            return;

        ClearOrders(target);
        foreach (var order in sprite.ActionStack.Orders)
        {
            if (order.Value == Act.StopMarker)
                break;

            target.ActionStack.Push(order);
        }
    }

    private void SpriteDestroyUnit(int id, Vector2 point)
    {
        if (id <= -1 || Definitions.Find((KnownObject)id) is null)
            return;

        var found = GetSpriteScr(SpriteSearch.Id + id, point);

        if (found is not null)
        {
            DeleteSprite(found);
        }
    }

    private int SpriteGetItemType(Sprite sprite, int type)
    {
        if (sprite.Items.Count == 0)
            return NoItem;

        var mask = type != 0 ? (OldSpriteType)type : OldSpriteType.All;
        int matching = 0;
        foreach (int item in sprite.Items)
        {
            if ((GetItemType(item) & mask) != OldSpriteType.None)
            {
                matching++;
            }
        }

        int pick = _rng.Next();

        if (matching == 0)
            return NoItem;

        pick %= matching;
        foreach (int item in sprite.Items)
        {
            if ((GetItemType(item) & mask) != OldSpriteType.None && --pick < 0)
                return item;
        }

        return NoItem;
    }

    private int SpriteDamage(Sprite sprite, int damage, int var2)
    {
        int maxHp = sprite.Definition.GetArmyMaxHp((int)sprite.Army);

        if (maxHp <= sprite.Hp && damage < 0)
            return 1;

        if (sprite.Animation >= SpriteAnimation.Death)
            return 0;

        int handler = sprite.Definition.DamageHandler;

        if (handler >= 0 && CallScript(handler, sprite, var2, damage) != 0)
            return 0;

        if (sprite.Definition.MaxHp != 0)
        {
            ChangeHp(sprite, sprite.Hp - damage);
        }

        if (maxHp < sprite.Hp && damage < 0)
        {
            sprite.Hp = maxHp;
        }

        if (damage <= 0)
            return 0;

        if (sprite.Definition.GetFrameCount(SpriteAnimation.Close) != 0 && sprite.Animation is SpriteAnimation.Stand or SpriteAnimation.Go)
        {
            sprite.ChangeAnimation(SpriteAnimation.Close);
            return 0;
        }

        int closeHandler = sprite.Definition.StateHandlers[(int)SpriteAnimation.Close];

        if (closeHandler >= 0 && CallScript(closeHandler, sprite, var2) != 0)
            return 0;

        int closeSound = sprite.Definition.Sounds[(int)SpriteAnimation.Close];

        if (closeSound != 0)
        {
            PlaySfx(sprite, closeSound);
        }

        if (sprite.Definition.Children[(int)SpriteAnimation.Close] != 0)
        {
            CreateChildAs(sprite, SpriteAnimation.Close);
        }

        return 0;
    }

    private void SpriteRepair(Sprite sprite)
    {
        DestroyLink(sprite, Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)SpriteAnimation.Wound])));
        sprite.Hp = sprite.Definition.GetArmyMaxHp((int)sprite.Army);
        var link = sprite.Link;

        if (link is not null && link.Definition == sprite.Definition.LinkDefinition)
        {
            Action(link, ActionCode.Repair);
        }
        else
        {
            CreateLink(sprite);
        }
    }

    private OldSpriteType GetItemType(int item) => Definitions.Find((KnownObject)item)?.Object.SpriteType.ToOldSpriteType() ?? OldSpriteType.None;

    [EngineRoutine(0x00445be0)]
    private void ClearOrders(Sprite sprite)
    {
        for (int i = 0; i < sprite.ActionStack.Count; i++)
        {
            var order = sprite.ActionStack[i];

            if (order is { Code: ActionCode.RestoreCommand, Target: not null })
            {
                Release(order.Target);
            }
        }

        sprite.ActionStack.Clear();
    }

    [EngineRoutine(0x004429f0)]
    private bool RemoveFromUpdates(Sprite sprite)
    {
        if (sprite.Link is not null)
        {
            RemoveFromUpdates(sprite.Link);
        }

        return _updates.Remove(sprite.Definition.Layer, sprite);
    }

    [EngineRoutine(0x00442a10)]
    private void AddToUpdates(Sprite sprite)
    {
        if (sprite.Link is not null)
        {
            AddToUpdates(sprite.Link);
        }

        AddToLayer(sprite);
    }

    [EngineRoutine(0x00442ad0)]
    private void ChangeArmy(Sprite sprite, int army)
    {
        int previous = (int)sprite.Army & SpriteDefinition.ArmyMask;
        sprite.Army = (Army)(army & SpriteDefinition.ArmyMask);
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

        if (definition.Counts[previous] != 0)
        {
            definition.Counts[previous]--;
        }

        definition.LastCreatedAt = WallTime;
        definition.Counts[current]++;
    }
}

using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

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
    private const OldSpriteType AnySpriteType = (OldSpriteType)((1 << 24) - 1);
    private const int NoItem = -1;
    private const int RegionLayer = 10;

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
        SendAction(sprite, (int)code, var1, var2, var3, target);

    private int SendAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target) =>
        sprite == CursorSprite ? CursorAction(sprite, act, var1, var2, var3, target) : DispatchClassAction(sprite, act, var1, var2, var3, target);

    private int DispatchClassAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target) =>
        sprite.Definition.Object.SpriteClass switch
        {
            SpriteClass.Terrain or SpriteClass.Object => TerrainAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Unit => UnitAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Creature => CreatureAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Cannon => CannonAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Building => BuildingAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Avia => AviaAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Man => ManAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Frame => FrameAction(sprite, act, var1, var2, var3, target),
            SpriteClass.Text when sprite.Text is not null => TextAction(sprite, act, var1, var2, var3, target),
            _ => SpriteAction(sprite, act, var1, var2, var3, target),
        };

    [EngineRoutine(0x00454800)]
    private int ManAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.CoorAttack:
            {
                var weapon = sprite.Definition.LinkDefinition;
                var torso = sprite.Link;

                if (torso is null || torso.Definition != weapon || weapon.FightChild == 0 || weapon.Object.WeaponIndex == 0 || (uint)torso.Timer > ReloadTimerLimit ||
                    torso.Animation == SpriteAnimation.Fight && torso.Frame <= torso.LastFrame)
                    return 0;

                var goal = ConstructGoal(Weapons.GetCursorPoint(sprite, new Vector2(var1, var2), weapon));
                SpriteKinematics.SetCommand(torso, Command.OneAttack, goal);

                return 0;
            }
            case ActionCode.ChangeVid:
            {
                if ((int)sprite.Definition.Id < PlayerIdLimit)
                {
                    sprite.SlotAmmo[sprite.WeaponSlot] = UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null);
                }

                UnitAction(sprite, act, var1, var2, var3, target);

                if ((int)sprite.Definition.Id > PlayerIdLimit)
                {
                    var armed = sprite.Definition.LinkDefinition;
                    int capacity = armed is not null && armed.FightChild != 0 && armed.Object.WeaponIndex != 0 ? armed.AmmoCapacity : sprite.Definition.AmmoCapacity;
                    UnitAction(sprite, (int)ActionCode.AddAmmo, capacity - UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null), 0, 0, null);

                    return 0;
                }

                var armorItem = sprite.GetItemNumber(WornArmorItemBase) >= 0 ? WornArmorItemBase
                    : sprite.GetItemNumber(WornArmorItemBase + 1) >= 0 ? WornArmorItemBase + 1
                    : sprite.GetItemNumber(WornArmorItemBase + 2) >= 0 ? WornArmorItemBase + 2
                    : 0;

                if (armorItem != 0)
                {
                    var position = new Vector3(sprite.Position.X, sprite.Position.Y, sprite.Position.Z + sprite.Definition.Object.LinkOffset.Z);
                    var armor = AddSprite(Definitions.Find((KnownObject)(armorItem - WornArmorItemBase + (int)KnownObject.WornGreenArmor))!, position, sprite.Direction, null);
                    AddLink(sprite.Link!, armor);
                }

                UnitAction(sprite, (int)ActionCode.AddAmmo, sprite.SlotAmmo[sprite.WeaponSlot] - UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null), 0, 0, null);

                return 0;
            }
            case ActionCode.AddItem:
            {
                if ((KnownObject)var1 is KnownObject.Dynamite or KnownObject.ExtraLife)
                {
                    sprite.InsertItem(var1);

                    return 0;
                }

                if (!sprite.InsertUniqueItem(var1))
                    return 0;

                int slot = var1 - Sprite.WeaponItemBase;

                if (slot is < 0 or >= Sprite.WeaponSlots)
                    return 0;

                if (UnitAction(sprite, (int)ActionCode.GetAmmo, 0, 0, 0, null) == 0 || sprite.WeaponSlot < slot || slot == 0)
                {
                    Weapons.ChangeWeapon(sprite, slot);
                }

                return 0;
            }
            case ActionCode.Damage:
            {
                int damage = var1;

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
                        ChangeHp(sprite, sprite.Definition.ArmyMaxHp[(int)sprite.Army & ArmyMask]);
                        AddSprite(Definitions.Find(KnownObject.RescueMedkitEffect)!, sprite.Position + Vector3.UnitZ * RescueEffectHeight, default, sprite);

                        return 0;
                    }
                }

                return SpriteAction(sprite, act, damage, var2, var3, target);
            }
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
            {
                if (sprite.Animation >= SpriteAnimation.Death)
                    return 0;

                if (sprite.HasGoal || sprite.Link is { HasGoal: true })
                {
                    sprite.AttackResult = sprite.AttackTact(Math.Max(FrameTime, sprite.Definition.Layout.GetFrameDuration(sprite.Animation)));
                }

                sprite.ChangeAnimation(!sprite.Speed.IsExactlyZero() ? SpriteAnimation.Go : SpriteAnimation.Stand);

                return 0;
            }
        }

        return UnitAction(sprite, act, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044ca70)]
    private int BuildingAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.BackupCommand when sprite.Command == Command.Build && _builds.TryGetValue(sprite, out var building):
                sprite.ActionStack.Push(new Act((int)ActionCode.BuildUnit, (int)building.Definition.Id, 0, 0));
                SpriteKinematics.SetCommand(sprite, Command.None);

                return 0;
            case ActionCode.BuildUnit:
            {
                bool noItem = false;

                if (var1 == 0)
                {
                    var1 = Action(sprite, ActionCode.GetItemType, (int)BuiltSpriteType);
                    noItem = var1 == 0;
                }

                var definition = var1 >= 0 ? Definitions.Find((KnownObject)var1) : null;

                if (!(noItem || sprite.Command == Command.None) || definition is null)
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
                var built = AddSprite(pending.Definition, position, sprite.Direction, null);

                if (built is not null)
                {
                    Action(sprite, ActionCode.CopyStackTo, target: built);
                }

                _builds.Remove(sprite);
                sprite.ChangeAnimation(SpriteAnimation.Stand);

                return 0;
            }
        }

        return UnitAction(sprite, act, var1, var2, var3, target);
    }

    [EngineRoutine(0x0044ceb0)]
    private int AviaAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target) =>
        UnitAction(sprite, act, var1, var2, var3, target);

    [EngineRoutine(0x00448950)]
    private int CannonAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
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
    private int CreatureAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        if (code == ActionCode.Damage && var1 > 0)
        {
            var center = sprite.Position.AsVector2();
            var reach = new Vector2(CreatureAlertReach);
            for (var other = Collector.First(center - reach, center + reach); other is not null; other = Collector.NextHashInBox())
            {
                if (other.Definition.Object.SpriteClass == SpriteClass.Creature && center.MeasureOctagonalDistance(other.Position.AsVector2()) < CreatureAlertReach)
                {
                    Motion.StartMove(other);
                }
            }
        }

        if (code != ActionCode.NextCommand)
            return UnitAction(sprite, act, var1, var2, var3, target);

        if (sprite.Animation >= SpriteAnimation.Death)
            return 0;

        if (sprite.Animation is SpriteAnimation.Fight or SpriteAnimation.Wound)
        {
            sprite.ChangeAnimation(SpriteAnimation.Stand);
        }

        if (sprite is { SeeksRegion: true, Region: null })
        {
            sprite.SeeksRegion = false;
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
            var result = sprite.AttackTact(Math.Max(FrameTime, sprite.Definition.Layout.GetFrameDuration(sprite.Animation)));
            sprite.AttackResult = result;

            if (result == AttackResult.Approach && sprite.Speed.IsExactlyZero() && (sprite.ArmedLink is { HasGoal: true } || sprite.HasGoal))
            {
                SpriteKinematics.SetCommand(sprite, Command.None);
            }

            if (result is AttackResult.Pursue or AttackResult.NoTarget && sprite.IsAggressive && sprite.Command is Command.None or Command.Move)
            {
                var enemy = _brain.SeekEnemy(sprite, GetFlagman(0));

                if (enemy is not null)
                {
                    SpriteKinematics.SetCommand(sprite, Command.OneAttack, enemy);
                }
            }
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
                else if (sprite.Speed.IsExactlyZero() && _rng.Next() % 3 == 0 && (sprite.Definition.Object.FrameCounts[(int)SpriteAnimation.Unload] != 0 || sprite.Definition.Children[(int)SpriteAnimation.Unload] != 0 || sprite.Definition.Object.Sounds[(int)SpriteAnimation.Unload] != 0))
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

    [EngineRoutine(0x0044d7b0)]
    private Sprite? FindRegion(Vector2 point)
    {
        var list = _updates.GetLayer(RegionLayer);

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var sprite = list[i];

            if (sprite is null || (sprite.Definition.Object.SpriteType.ToOldSpriteType() & OldSpriteType.Region) == OldSpriteType.None || sprite.Definition.Object.SpriteClass != SpriteClass.Region)
                continue;

            float halfWidth = sprite.RegionSize.X * 0.5f;

            if (sprite.Position.X - halfWidth > point.X || point.X > halfWidth + sprite.Position.X)
                continue;

            float halfHeight = sprite.RegionSize.Y * 0.5f;

            if (sprite.Position.Y - halfHeight <= point.Y && point.Y <= halfHeight + sprite.Position.Y)
                return sprite;
        }

        return null;
    }

    [EngineRoutine(0x00447500)]
    private int UnitAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        switch (code)
        {
            case ActionCode.Damage:
            {
                int result = TerrainAction(sprite, act, var1, var2, var3, target);

                if (var1 >= 0 && sprite.Command == Command.None && !sprite.Definition.Speed.IsExactlyZero())
                {
                    var wander = sprite.Position.AsVector2() + new Angle((byte)_rng.Next()).Vector * WanderReach;
                    _brain.MoveTo(sprite, ConstructGoal(new Vector3(wander, sprite.Position.Z)));
                }

                return result;
            }
            case ActionCode.NextCommand:
            {
                if (sprite.Animation >= SpriteAnimation.Death)
                    return 0;

                if (sprite.Speed.IsExactlyZero())
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
                        var enemy = _brain.SeekEnemy(sprite, GetFlagman(0));

                        if (enemy is not null)
                        {
                            SpriteKinematics.SetCommand(link, Command.OneAttack, enemy);
                        }
                    }
                }

                if (result == AttackResult.Pursue && sprite.IsActive && sprite.Speed.IsExactlyZero())
                {
                    Motion.StartMove(sprite);
                }

                if (result == AttackResult.NoOrder && sprite is { IsAggressive: true, IsActive: true } ||
                    result is AttackResult.Pursue or AttackResult.NoTarget && sprite is { IsAggressive: true, Command: Command.None or Command.Move or Command.OneAttack } &&
                    (sprite.LastFrame > sprite.FirstFrame || sprite.Timer != 0 || _rng.Next(IdleScanOdds) == 0))
                {
                    var enemy = _brain.SeekEnemy(sprite, GetFlagman(0));

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
    private int TerrainAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        if ((ActionCode)act != ActionCode.Repair)
            return SpriteAction(sprite, act, var1, var2, var3, target);

        Repair(sprite, true);

        return 0;
    }

    [EngineRoutine(0x0044bf10)]
    private void Repair(Sprite sprite, bool relink)
    {
        var army = (int)sprite.Army & ArmyMask;
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
                var effect = Definitions.Find(RelinkEffectId);

                if (effect is not null)
                {
                    CreateSprite(effect, sprite.Position, default);
                }

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

        if (definition.Object.SpriteClass == SpriteClass.Linker && definition.FightChild != 0)
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
        if (target is null || target.Definition == Definitions.Empty || sprite.CanAttackThisSprite(target) || definition.Object.SpriteClass == SpriteClass.Cannon ||
            Action(sprite, ActionCode.IsTrain) != 0)
        {
            SpriteKinematics.SetCommand(sprite, Command.Attack, target);
        }
    }

    [EngineRoutine(0x00440830)]
    private int SpriteAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)(act & byte.MaxValue);
        switch (code)
        {
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
            {
                var armed = (sprite.ArmedLink ?? sprite).Definition;
                int y = var2;
                float z;

                if (armed.Targets == SpriteType.Unit)
                {
                    z = Movement.GetHeight(new Vector2(var1, var2 + UnitAimLift)) + UnitAimLift;
                    y += (int)z;
                }
                else
                {
                    z = Movement.GetHeight(new Vector2(var1, var2)) + GroundAimLift;
                    y += (int)z - (int)GroundAimLift;
                }

                SpriteKinematics.SetCommand(sprite, Command.OneAttack, ConstructGoal(new Vector3(var1, y, z)));
                break;
            }
            case ActionCode.Random:
            {
                int duration = (byte)sprite.Definition.Layout.GetFrameDuration(sprite.Animation);

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

                break;
            }
            case ActionCode.GetGoal:
                return GetSpriteHandle(sprite.Goal);
            case ActionCode.SetGoalCoor:
                sprite.Goal = ConstructGoal(new Vector3(var1, var2, var3));
                break;
            case ActionCode.GetBattleRange:
            {
                var armedLink = sprite.ArmedLink;

                return (int)(sprite.Definition.BattleRange.IsExactlyZero() && armedLink is not null ? armedLink.Definition.BattleRange : sprite.Definition.BattleRange);
            }
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
            {
                int id = var1 != 0 ? var1 : Action(sprite, ActionCode.GetItemType, (int)BuiltSpriteType);

                if (id <= 0)
                    break;

                var definition = Definitions.Find((KnownObject)id);

                if (definition is null)
                    break;

                var built = AddSprite(definition, GetBuildPosition(sprite, var2, var3), sprite.Direction, null);

                if (built is not null)
                {
                    Action(sprite, ActionCode.CopyStackTo, target: built);
                }

                break;
            }
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
            {
                if (!sprite.Speed.IsExactlyZero())
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
            {
                var flagman = GetFlagman(ControlledPlayer);

                if (flagman is null || flagman.Position.AsVector2().MeasureOctagonalDistance(new Vector2(var1, var2)) >= var3)
                {
                    sprite.ActionStack.Push(new Act((int)code, var1, var2, var3));
                }

                break;
            }
            case ActionCode.ChangeVid:
            {
                var definition = Definitions.Find((KnownObject)var1);

                if (definition is null || definition == sprite.Definition)
                    break;

                DropLinks(sprite);
                RemoveFromUpdates(sprite);
                var state = var2 < 0 ? sprite.Animation : (SpriteAnimation)var2;
                var direction = sprite.Direction;
                sprite.ChangeVid(definition);
                Collector.ChangeCoor(sprite);
                AddToUpdates(sprite);

                CreateLink(sprite);

                sprite.ChangeAnimation(state);
                sprite.ChangeDirection(direction);
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
                if (target is null || target == sprite)
                    break;

                ClearOrders(target);
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
            {
                if (sprite.Items.Count == 0)
                    return NoItem;

                var mask = var1 != 0 ? (OldSpriteType)var1 : AnySpriteType;
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
            case ActionCode.Damage:
            {
                int maxHp = sprite.Definition.ArmyMaxHp[(int)sprite.Army & ArmyMask];

                if (maxHp <= sprite.Hp && var1 < 0)
                    return 1;

                if (sprite.Animation >= SpriteAnimation.Death)
                    break;

                int handler = sprite.Definition.DamageHandler;

                if (handler >= 0 && CallScript(handler, sprite, var2, var1) != 0)
                    break;

                if (sprite.Definition.MaxHp != 0)
                {
                    ChangeHp(sprite, sprite.Hp - var1);
                }

                if (maxHp < sprite.Hp && var1 < 0)
                {
                    sprite.Hp = maxHp;
                }

                if (var1 <= 0)
                    break;

                if (sprite.Definition.Layout.GetFrameCount(SpriteAnimation.Close) != 0 && sprite.Animation is SpriteAnimation.Stand or SpriteAnimation.Go)
                {
                    sprite.ChangeAnimation(SpriteAnimation.Close);
                    break;
                }

                int closeHandler = sprite.Definition.StateHandlers[(int)SpriteAnimation.Close];

                if (closeHandler >= 0 && CallScript(closeHandler, sprite, var2) != 0)
                    break;

                int closeSound = sprite.Definition.Object.Sounds[(int)SpriteAnimation.Close];

                if (closeSound != 0)
                {
                    PlaySfx(sprite, closeSound);
                }

                if (sprite.Definition.Children[(int)SpriteAnimation.Close] != 0)
                {
                    var previous = sprite.Animation;
                    sprite.SetAnimationValue(SpriteAnimation.Close);
                    CreateChild(sprite);
                    sprite.SetAnimationValue(previous);
                }

                break;
            }
            case ActionCode.Repair:
            {
                DestroyLink(sprite, Definitions.Find((KnownObject)Math.Abs(sprite.Definition.Children[(int)SpriteAnimation.Wound])));
                sprite.Hp = sprite.Definition.ArmyMaxHp[(int)sprite.Army & ArmyMask];
                var link = sprite.Link;

                if (link is not null && link.Definition == sprite.Definition.LinkDefinition)
                {
                    Action(link, ActionCode.Repair);
                }
                else
                {
                    CreateLink(sprite);
                }

                break;
            }
            case ActionCode.GetHp:
                return sprite.Hp;
            case ActionCode.SetHp:
                ChangeHp(sprite, var1 != 0 ? var1 : sprite.Definition.ArmyMaxHp[(int)sprite.Army & ArmyMask] * var2 / Percent);
                break;
            case ActionCode.SetBehave:
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
                Session.Logger.Error(SpriteLogTag, LogError.Text, 10, default, (int)sprite.Definition.Id);
                break;
        }

        return 0;
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

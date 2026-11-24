using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    private protected const OldSpriteType BuiltSpriteType = OldSpriteType.Unit;
    private const float GroundAimLift = 19;
    private const int HpRounding = 0xff;
    private const int HpShift = 8;
    private const int NoItem = -1;
    private const int QuarterTurn = Angle.QuarterTurn;
    private const float ScriptSpeedScale = 1000;
    private const float SpeedPerMillisecond = 0.001f;
    private const float UnitAimLift = 80;

    [EngineRoutine(0x00440830)]
    internal int SpriteAction(int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)(act & byte.MaxValue);
        switch (code)
        {
            case ActionCode.GetAmmo or ActionCode.AddAmmo:
                return Owner?.Action(act, var1, var2, var3, target) ?? 0;
            case ActionCode.Salut:
                if (Animation != SpriteAnimation.Fight)
                {
                    ChangeAnimation(SpriteAnimation.Salut);
                }

                break;
            case ActionCode.Death:
                if (Definition.Damage != 0)
                {
                    Hp = 0;
                    ApplyBlast();
                }

                break;
            case ActionCode.CoorAttack:
                CoorAttackCase(new Point(var1, var2));
                break;
            case ActionCode.Random:
                RandomCase();
                break;
            case ActionCode.GetGoal:
                return Map.GetSpriteHandle(Goal);
            case ActionCode.SetGoalCoor:
                Goal = Map.ConstructGoal(new Vector3(var1, var2, var3));
                break;
            case ActionCode.GetBattleRange:
                return BattleRange();
            case ActionCode.GetLink:
                return Map.GetSpriteHandle(Link);
            case ActionCode.GetUplink:
                return Map.GetSpriteHandle(Owner);
            case ActionCode.GetZSpeed:
                return (int)(VerticalSpeed * ScriptSpeedScale);
            case ActionCode.GetSpeed:
                return (int)(Speed * ScriptSpeedScale);
            case ActionCode.Attack:
                Map.Attack(this, target);
                break;
            case ActionCode.Move:
                Move(Map.ConstructGoal(new Vector3(var1, var2, var3)));
                break;
            case ActionCode.MoveTo:
                Move(target);
                break;
            case ActionCode.BuildUnit:
                BuildUnitCase(var1, new Point(var2, var3));
                break;
            case ActionCode.PlaySfx:
                Map.PlaySfx(this, var1);
                break;
            case ActionCode.Stop:
                Stop();

                if (var1 != 0)
                {
                    Speed = 0;
                }

                break;
            case ActionCode.Pause:
                Pause(var1, var2);
                break;
            case ActionCode.Turn:
                RotateTact(new Angle(unchecked((byte)var1)), Definition.GetVisualFrameDuration(Animation));
                break;
            case ActionCode.ClearCommand:
                SetCommand(Command.None);
                break;
            case ActionCode.FlagmanTrigger:
                FlagmanTriggerCase(new Point(var1, var2), var3);
                break;
            case ActionCode.ChangeVid:
                ChangeVidCase(var1, var2);
                break;
            case ActionCode.ChangeCoor:
                ChangeCoor(new Vector3(var1, var2, var3));
                break;
            case ActionCode.ChangeDirection:
                ChangeDirection(new Angle(unchecked((byte)var1)));
                break;
            case ActionCode.ChangeAnimation:
                ChangeAnimation((SpriteAnimation)var1);
                break;
            case ActionCode.GotoStack:
                ActionStack.SetCount(var1 + 1);
                Frame = LastFrame;
                break;
            case ActionCode.BackupCommand:
                ActionStack.Push(new Act((int)Command << 8 | (int)ActionCode.RestoreCommand, Map.GetSpriteHandle(Goal), 0, 0, Goal));
                Goal?.AddRef();
                break;
            case ActionCode.RestoreCommand:
                SetCommand((Command)(act >> 8 & byte.MaxValue), target);

                if (target is not null)
                {
                    Map.Release(target);
                }

                break;
            case ActionCode.ClearStack:
                ActionStack.Clear();
                break;
            case ActionCode.GetStackCount:
                return ActionStack.Count;
            case ActionCode.GetPercentHp:
                return GetPercentHp() * 100 / 0xff;
            case ActionCode.CopyStackTo:
                CopyStackToCase(target);
                break;
            case ActionCode.StopStack:
                ActionStack.Push(new Act((int)code, var1, var2, var3));
                Map.Action(this, ActionCode.NextCommand);
                break;
            case ActionCode.NextCommand:
                NextCommandCase();
                break;
            case ActionCode.DestroyUnit:
                DestroyUnitCase(var1, new Vector2(var2, var3));
                break;
            case ActionCode.AddItem:
                InsertItem(var1);
                break;
            case ActionCode.DeleteItem:
                if (RemoveItemAt(GetItemNumber(var1)))
                    return 1;

                break;
            case ActionCode.HaveItem:
                return GetItemNumber(var1) >= 0 ? 1 : 0;
            case ActionCode.DeleteAllItems:
                Items.Clear();
                break;
            case ActionCode.GetItem:
                return var1 >= 0 && var1 < Items.Count ? Items[var1] : 0;
            case ActionCode.GetItemType:
                return GetItemTypeCase(var1);
            case ActionCode.Damage:
                return ActionDamage(var1, var2);
            case ActionCode.Repair:
                RepairCase();
                break;
            case ActionCode.GetHp:
                return Hp;
            case ActionCode.SetHp:
                Map.ChangeHp(this, var1 != 0 ? var1 : Definition.GetArmyMaxHp((int)Army) * var2 / Map.Percent);
                break;
            case ActionCode.SetBehave:
                break;
            case ActionCode.GetArmy:
                return (int)Army & SpriteDefinition.ArmyMask;
            case ActionCode.SetArmy:
                ChangeArmy(var1);
                break;
            case ActionCode.SetInvisible:
                SetInvisible(var1 != 0);
                break;
            case ActionCode.GetTimer:
                return Timer;
            case ActionCode.SetTimer:
                Timer = var1;
                break;
            case ActionCode.SetZSpeed:
                VerticalSpeed = var1 * SpeedPerMillisecond;
                break;
            case ActionCode.SetSpeed:
                Speed = var1 * SpeedPerMillisecond;
                break;
            case ActionCode.GetCommand:
                return (int)Command;
            case ActionCode.SetDeathTimer:
                Lifetime = var1;
                break;
            case (ActionCode)Act.ForgottenOrder or ActionCode.SetQuest or ActionCode.WhileNotQuest or ActionCode.SetText or ActionCode.SetTextCount or
                ActionCode.SetFile or ActionCode.Select or ActionCode.ChangeCoorZ:
                break;
            default:
                Map.Session.Logger.Error($"SPRITE {(int)Definition.Id}", LogError.Text, 10);
                break;
        }

        return 0;
    }

    [EngineRoutine(0x00442ad0)]
    internal void ChangeArmy(int army)
    {
        int previous = (int)Army & SpriteDefinition.ArmyMask;
        Army = (Army)(army & SpriteDefinition.ArmyMask);
        int current = (int)Army;

        Link?.ChangeArmy(army);

        var definition = Definition;
        int newMax = definition.ArmyMaxHp[current];
        int oldMax = definition.ArmyMaxHp[previous];

        if (newMax != oldMax)
        {
            int scaled = (Hp << HpShift) / oldMax * newMax;
            Map.ChangeHp(this, (scaled + (scaled >> 31 & HpRounding)) >> HpShift);
        }

        if (definition.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
        {
            SetInvisible(current != Map.ControlledPlayer);
        }

        if (definition.Counts[previous] != 0)
        {
            definition.Counts[previous]--;
        }

        definition.LastCreatedAt = Map.WallTime;
        definition.Counts[current]++;
    }

    private protected void AddBuiltUnit(SpriteDefinition definition, Vector3 position)
    {
        var built = Map.AddSprite(definition, position, Direction);

        if (built is not null)
        {
            Map.Action(this, ActionCode.CopyStackTo, target: built);
        }
    }

    private void SetInvisible(bool isInvisible)
    {
        IsInvisible = isInvisible;

        if (isInvisible)
        {
            Link?.InvisibleOn();
        }
        else
        {
            Link?.InvisibleOff();
        }
    }

    private int BattleRange()
    {
        var armedLink = ArmedLink;

        return (int)(Definition.BattleRange.IsExactlyZero() && armedLink is not null ? armedLink.Definition.BattleRange : Definition.BattleRange);
    }

    private void NextCommandCase()
    {
        var state = Animation;

        if (state >= SpriteAnimation.Death)
            return;

        if (Speed.IsExactlyZero())
        {
            if (state > SpriteAnimation.RRotate && state != SpriteAnimation.StandOpen)
            {
                ChangeAnimation(SpriteAnimation.Stand);
            }
        }
        else if (state != SpriteAnimation.Go)
        {
            ChangeAnimation(SpriteAnimation.Go);
        }
    }

    private void CoorAttackCase(Point point)
    {
        var armed = (ArmedLink ?? this).Definition;
        int y = point.Y;
        float z;

        if (armed.Targets == SpriteType.Unit)
        {
            z = Map.GetGroundZ(new Vector2(point.X, point.Y + UnitAimLift)) + UnitAimLift;
            y += (int)z;
        }
        else
        {
            z = Map.GetGroundZ(new Vector2(point.X, point.Y)) + GroundAimLift;
            y += (int)z - (int)GroundAimLift;
        }

        SetCommand(Command.OneAttack, Map.ConstructGoal(new Vector3(point.X, y, z)));
    }

    private void RandomCase()
    {
        int duration = (byte)Definition.GetVisualFrameDuration(Animation);

        if (Map.Random.Next() % 5 == 0)
        {
            ChangeAnimation(SpriteAnimation.Stand);
        }
        else if (Map.Random.Next() % 5 == 0)
        {
            ChangeAnimation(SpriteAnimation.Unload);
        }
        else if (Animation == SpriteAnimation.LRotate && Map.Random.Next() % 3 != 0)
        {
            RotateTact(Direction - QuarterTurn, duration);
        }
        else if (Animation == SpriteAnimation.RRotate && Map.Random.Next() % 3 != 0)
        {
            RotateTact(Direction + QuarterTurn, duration);
        }
        else if (Map.Random.Next() % 4 == 0)
        {
            RotateTact(Map.Random.Next() % 2 == 0 ? Direction + QuarterTurn : Direction - QuarterTurn, duration);
        }
    }

    private void BuildUnitCase(int id, Point offset)
    {
        if (id == 0)
        {
            id = Map.Action(this, ActionCode.GetItemType, (int)BuiltSpriteType);
        }

        if (id <= 0)
            return;

        var definition = Map.Definitions.Find((KnownObject)id);

        if (definition is null)
            return;

        AddBuiltUnit(definition, Map.GetBuildPosition(this, offset));
    }

    private void Pause(int time, int spread)
    {
        if (!Speed.IsExactlyZero())
        {
            Stop();
        }

        int pause = time + Map.Random.Next() % (spread + 1);
        Timer = pause;
        SetCommand(pause != 0 ? Command.Pause : Command.None);
    }

    private void FlagmanTriggerCase(Point point, int range)
    {
        var flagman = Map.GetFlagman(Map.ControlledPlayer);

        if (flagman is null || flagman.Position.AsVector2().MeasureOctagonalDistance(new Vector2(point.X, point.Y)) >= range)
        {
            ActionStack.Push(new Act((int)ActionCode.FlagmanTrigger, point.X, point.Y, range));
        }
    }

    private void ChangeVidCase(int id, int animation)
    {
        var definition = Map.Definitions.Find((KnownObject)id);

        if (definition is null || definition == Definition)
            return;

        Map.DropLinks(this);
        Map.Collector.DeleteFromHash(this);
        Map.RemoveFromUpdates(this);
        var state = animation < 0 ? Animation : (SpriteAnimation)animation;
        var direction = Direction;
        ChangeVid(definition);
        Map.AddToUpdates(this);
        Map.Collector.InsertToHash(this);

        Map.CreateLink(this);

        ChangeAnimation(state);
        ChangeDirection(direction);
    }

    private void CopyStackToCase(Sprite? target)
    {
        if (target is null || target == this)
            return;

        target.ClearOrders();
        foreach (var order in ActionStack.Orders)
        {
            if (order.Value == Act.StopMarker)
                break;

            target.ActionStack.Push(order);
        }
    }

    private void DestroyUnitCase(int id, Vector2 point)
    {
        if (id <= -1 || Map.Definitions.Find((KnownObject)id) is null)
            return;

        var found = Map.GetSpriteScr(SpriteSearch.Id + id, point);

        if (found is not null)
        {
            Map.DeleteSprite(found);
        }
    }

    private int GetItemTypeCase(int type)
    {
        if (Items.Count == 0)
            return NoItem;

        var mask = type != 0 ? (OldSpriteType)type : OldSpriteType.All;
        int matching = 0;
        foreach (int item in Items)
        {
            if ((GetItemType(item) & mask) != OldSpriteType.None)
            {
                matching++;
            }
        }

        int pick = Map.Random.Next();

        if (matching == 0)
            return NoItem;

        pick %= matching;
        foreach (int item in Items)
        {
            if ((GetItemType(item) & mask) != OldSpriteType.None && --pick < 0)
                return item;
        }

        return NoItem;
    }

    private int ActionDamage(int damage, int var2)
    {
        int maxHp = Definition.GetArmyMaxHp((int)Army);

        if (maxHp <= Hp && damage < 0)
            return 1;

        if (Animation >= SpriteAnimation.Death)
            return 0;

        int handler = Definition.DamageHandler;

        if (handler >= 0 && Map.CallScript(handler, this, var2, damage) != 0)
            return 0;

        if (Definition.MaxHp != 0)
        {
            Map.ChangeHp(this, Hp - damage);
        }

        if (maxHp < Hp && damage < 0)
        {
            Hp = maxHp;
        }

        if (damage <= 0)
            return 0;

        if (Definition.GetFrameCount(SpriteAnimation.Close) != 0 && Animation is SpriteAnimation.Stand or SpriteAnimation.Go)
        {
            ChangeAnimation(SpriteAnimation.Close);
            return 0;
        }

        int closeHandler = Definition.StateHandlers[(int)SpriteAnimation.Close];

        if (closeHandler >= 0 && Map.CallScript(closeHandler, this, var2) != 0)
            return 0;

        int closeSound = Definition.Sounds[(int)SpriteAnimation.Close];

        if (closeSound != 0)
        {
            Map.PlaySfx(this, closeSound);
        }

        if (Definition.Children[(int)SpriteAnimation.Close] != 0)
        {
            Map.CreateChildAs(this, SpriteAnimation.Close);
        }

        return 0;
    }

    private void RepairCase()
    {
        Map.DestroyLink(this, Map.Definitions.Find((KnownObject)Math.Abs(Definition.Children[(int)SpriteAnimation.Wound])));
        Hp = Definition.GetArmyMaxHp((int)Army);
        var link = Link;

        if (link is not null && link.Definition == Definition.LinkDefinition)
        {
            Map.Action(link, ActionCode.Repair);
        }
        else
        {
            Map.CreateLink(this);
        }
    }

    private OldSpriteType GetItemType(int item) => Map.Definitions.Find((KnownObject)item)?.Object.SpriteType.ToOldSpriteType() ?? OldSpriteType.None;


    [EngineRoutine(0x00445be0)]
    private void ClearOrders()
    {
        for (int i = 0; i < ActionStack.Count; i++)
        {
            var order = ActionStack[i];

            if (order is { Code: ActionCode.RestoreCommand, Target: not null })
            {
                Map.Release(order.Target);
            }
        }

        ActionStack.Clear();
    }

    private void ApplyBlast()
    {
        var payload = Definition;

        if (payload.Damage == 0)
            return;

        var impact = Position;
        var point = impact.AsVector2();
        var reach = payload.Half + new Vector2(payload.BlastRadius);
        float heightReach = Math.Max(Map.MinBlastHeight, payload.Object.Size.Z);
        bool isScaled = payload.IsExplosive && !payload.BlastRadius.IsExactlyZero();
        int damage = payload.Damage;
        
        for (var sprite = Map.Collector.First(point - reach, point + reach); sprite is not null; sprite = Map.Collector.NextHashInBox())
        {
            if (sprite == this || sprite.Definition.MaxHp == 0 || payload.ShouldSkipOwnArmy && sprite.Army == Army)
                continue;

            var victim = sprite.Position;
            var offset = Vector2.Abs(victim.AsVector2() - point);
            float top = victim.Z + sprite.Definition.Object.Size.Z;

            if (offset.X >= reach.X + sprite.Definition.Half.X || offset.Y >= reach.Y + sprite.Definition.Half.Y ||
                MathF.Abs(impact.Z - victim.Z) >= heightReach + sprite.Definition.Object.Size.Z ||
                top < Map.GetGroundZ((victim.AsVector2() + point) * 0.5f) ||
                top < Map.GetGroundZ((point * 3 + victim.AsVector2()) * 0.25f) ||
                top < Map.GetGroundZ((victim.AsVector2() * 3 + point) * 0.25f))
                continue;

            float distance = victim.AsVector2().MeasureOctagonalDistance(point);

            if (isScaled && distance > payload.BlastRadius)
                continue;

            Map.Action(sprite, ActionCode.Damage, isScaled ? (int)(damage - damage * distance / payload.BlastRadius) : damage);
        }
    }
}

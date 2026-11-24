using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    private protected const OldSpriteType BuiltSpriteType = OldSpriteType.Unit;
    private protected const int OldArmyVersion = 7;
    private const float GroundAimLift = 19;
    private const int HpRounding = 0xff;
    private const int HpShift = 8;
    private const int ItemsVersion = 12;
    private const int NoItem = -1;
    private const int OldAttackAct = 0x27;
    private const int OldMoveAct = 0x28;
    private const int OldStackActionSize = 12;
    private const int OldStopStackAct = 0x2f;
    private const int QuarterTurn = Angle.QuarterTurn;
    private const float ScriptSpeedScale = 1000;
    private const float SpeedPerMillisecond = 0.001f;
    private const float UnitAimLift = 80;

    [EngineRoutine(0x00440830)]
    internal int SpriteAction(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)((int)act & byte.MaxValue);
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
                if (Vid.Damage != 0)
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
                Attack(target);
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
                PlaySfx(var1);
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
                RotateTact(new Angle(unchecked((byte)var1)), Vid.GetVisualFrameDuration(Animation));
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
                ActionStack.Count = var1 + 1;
                ActionStack.Expand(var1 + 1);
                Frame = LastFrame;
                break;
            case ActionCode.BackupCommand:
                ActionStack.Insert(new Act((int)Command << 8 | (int)ActionCode.RestoreCommand, Map.GetSpriteHandle(Goal), 0, 0, Goal));
                Goal?.AddRef();
                break;
            case ActionCode.RestoreCommand:
                SetCommand((Command)((int)act >> 8 & byte.MaxValue), target);

                if (target is not null)
                {
                    Map.Release(target);
                }

                break;
            case ActionCode.ClearStack:
                ActionStack.Release();
                break;
            case ActionCode.GetStackCount:
                return ActionStack.Count;
            case ActionCode.GetPercentHp:
                return GetPercentHp() * 100 / 0xff;
            case ActionCode.CopyStackTo:
                CopyStackToCase(target);
                break;
            case ActionCode.StopStack:
                ActionStack.Insert(new Act((int)code, var1, var2, var3));
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
                ChangeHp(var1 != 0 ? var1 : Vid.GetArmyMaxHp((int)Army) * var2 / Map.Percent);
                break;
            case ActionCode.SetBehave:
                break;
            case ActionCode.GetArmy:
                return (int)Army & Vid.ArmyMask;
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
                Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Text, 10);
                break;
        }

        return 0;
    }

    [EngineRoutine(0x00440830)]
    internal virtual int Action(ActionCode act, ref Resource stream, int version)
    {
        switch (act)
        {
            case ActionCode.Restore:
                int count = ActionStack.Count;
                stream.ReadInto(ref count);
                LoadOrders(stream.Read<StackAction>(count));

                if (version < ItemsVersion)
                    return 0;

                int itemCount = 0;
                stream.ReadInto(ref itemCount);
                var items = stream.Read<int>(itemCount);
                if (itemCount != 0)
                {
                    Items.Clear();
                    Items.AddRange(items);
                }

                break;
            case ActionCode.RestoreOld:
                short read = 0;
                stream.ReadInto(ref read);
                int oldCount = Math.Max((int)read, 0);
                var buffer = ActionStack.Buffer;
                var entries = new StackAction[Math.Max(buffer.Length, oldCount)];
                for (int i = 0; i < buffer.Length; i++)
                {
                    ref readonly var kept = ref buffer[i];
                    entries[i] = new StackAction(kept.Value, kept.Var1, kept.Var2, kept.Var3);
                }

                stream.Read(MemoryMarshal.AsBytes(entries.AsSpan())[..(oldCount * OldStackActionSize)]);
                if (version < OldArmyVersion)
                {
                    oldCount = 0;
                }

                for (int i = 0; i < oldCount; i++)
                {
                    ref readonly var entry = ref entries[i];
                    int oldAct = entry.Act & byte.MaxValue;
                    switch (oldAct)
                    {
                        case OldMoveAct:
                            oldAct = (int)ActionCode.Move;
                            break;
                        case OldAttackAct:
                            oldAct = (int)ActionCode.Attack;
                            break;
                        case OldStopStackAct:
                            oldAct = (int)ActionCode.StopStack;
                            break;
                        default:
                            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Unknown, oldAct, "actionStack.act restore"u8);
                            break;
                    }

                    entries[i] = new StackAction(oldAct, entry.Var1, entry.Var2, 0);
                }

                LoadOrders(entries.AsSpan(0, oldCount));
                if (version < OldArmyVersion)
                    return 0;

                byte army = 0;
                stream.ReadInto(ref army);
                ChangeArmy(army);
                break;
        }

        return 0;
    }

    private void LoadOrders(ReadOnlySpan<StackAction> stack)
    {
        ActionStack.Release();
        foreach (var action in stack)
        {
            ActionStack.Insert(Act.HasSpriteTarget(action.Code)
                ? new Act(action.Act, action.Var1, action.Var2, action.Var3, Map.Relation.Decode(action.Var1))
                : Act.FromAction(action));
        }

        for (int i = ActionStack.Count - 1; i >= 0; i--)
        {
            if (i != 0 && ActionStack[i].Value == Act.StopMarker && ActionStack[i - 1].Value == Act.StopMarker)
            {
                ActionStack[i] = ActionStack[--ActionStack.Count];
            }
        }
    }

    [EngineRoutine(0x00442ad0)]
    internal void ChangeArmy(int army)
    {
        int previous = (int)Army & Vid.ArmyMask;
        Army = (Army)(army & Vid.ArmyMask);
        int current = (int)Army;

        Link?.ChangeArmy(army);

        var vid = Vid;
        int newMax = vid.ArmyMaxHp[current];
        int oldMax = vid.ArmyMaxHp[previous];

        if (newMax != oldMax)
        {
            int scaled = (Hp << HpShift) / oldMax * newMax;
            ChangeHp((scaled + (scaled >> 31 & HpRounding)) >> HpShift);
        }

        if (vid.Object.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
        {
            SetInvisible(current != Map.ControlledPlayer);
        }

        if (vid.Counts[previous] != 0)
        {
            vid.Counts[previous]--;
        }

        vid.LastCreatedAt = Map.WallTime;
        vid.Counts[current]++;
    }

    private protected void AddBuiltUnit(Vid vid, Vector3 position)
    {
        var built = Map.CreateSprite(vid, position, Direction);

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

        return (int)(Vid.BattleRange.IsExactlyZero() && armedLink is not null ? armedLink.Vid.BattleRange : Vid.BattleRange);
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
        var armed = (ArmedLink ?? this).Vid;
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
        int duration = (byte)Vid.GetVisualFrameDuration(Animation);

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

        var vid = Map.FindVid((KnownObject)id);

        if (vid is null)
            return;

        AddBuiltUnit(vid, Map.GetBuildPosition(this, offset));
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
        var flagman = Map.Flagman(Map.ControlledPlayer);

        if (flagman is null || flagman.Position.AsVector2().MeasureOctagonalDistance(new Vector2(point.X, point.Y)) >= range)
        {
            ActionStack.Insert(new Act((int)ActionCode.FlagmanTrigger, point.X, point.Y, range));
        }
    }

    private void ChangeVidCase(int id, int animation)
    {
        var vid = Map.FindVid((KnownObject)id);

        if (vid is null || vid == Vid)
            return;

        Map.DropLinks(this);
        Map.Collector.DeleteFromHash(this);
        RemoveFromUpdates();
        var state = animation < 0 ? Animation : (SpriteAnimation)animation;
        var direction = Direction;
        ChangeVid(vid);
        AddToUpdates();
        Map.Collector.InsertToHash(this);

        CreateLink();

        ChangeAnimation(state);
        ChangeDirection(direction);
    }

    private void CopyStackToCase(Sprite? target)
    {
        if (target is null || target == this)
            return;

        target.ClearOrders();
        foreach (var order in ActionStack.Items)
        {
            if (order.Value == Act.StopMarker)
                break;

            target.ActionStack.Insert(order);
        }
    }

    private void DestroyUnitCase(int id, Vector2 point)
    {
        if (id <= -1 || Map.FindVid((KnownObject)id) is null)
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
        int maxHp = Vid.GetArmyMaxHp((int)Army);

        if (maxHp <= Hp && damage < 0)
            return 1;

        if (Animation >= SpriteAnimation.Death)
            return 0;

        int handler = Vid.DamageHandler;

        if (handler >= 0 && Map.ScriptRun(handler, this, var2, damage) != 0)
            return 0;

        if (Vid.MaxHp != 0)
        {
            ChangeHp(Hp - damage);
        }

        if (maxHp < Hp && damage < 0)
        {
            Hp = maxHp;
        }

        if (damage <= 0)
            return 0;

        if (Vid.GetFrameCount(SpriteAnimation.Close) != 0 && Animation is SpriteAnimation.Stand or SpriteAnimation.Go)
        {
            ChangeAnimation(SpriteAnimation.Close);
            return 0;
        }

        int closeHandler = Vid.StateHandlers[(int)SpriteAnimation.Close];

        if (closeHandler >= 0 && Map.ScriptRun(closeHandler, this, var2) != 0)
            return 0;

        int closeSound = Vid.Sounds[(int)SpriteAnimation.Close];

        if (closeSound != 0)
        {
            PlaySfx(closeSound);
        }

        if (Vid.Children[(int)SpriteAnimation.Close] != 0)
        {
            CreateChildAs(SpriteAnimation.Close);
        }

        return 0;
    }

    private void RepairCase()
    {
        DestroyLink(Vid.ChildVids[(int)SpriteAnimation.Wound]);
        Hp = Vid.GetArmyMaxHp((int)Army);
        var link = Link;

        if (link is not null && link.Vid == Vid.LinkVid)
        {
            Map.Action(link, ActionCode.Repair);
        }
        else
        {
            CreateLink();
        }
    }

    private OldSpriteType GetItemType(int item) => Map.FindVid((KnownObject)item)?.Object.SpriteType.ToOldSpriteType() ?? OldSpriteType.None;


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

        ActionStack.Release();
    }

    private void ApplyBlast()
    {
        var payload = Vid;

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
            if (sprite == this || sprite.Vid.MaxHp == 0 || payload.ShouldSkipOwnArmy && sprite.Army == Army)
                continue;

            var victim = sprite.Position;
            var offset = Vector2.Abs(victim.AsVector2() - point);
            float top = victim.Z + sprite.Vid.Object.Size.Z;

            if (offset.X >= reach.X + sprite.Vid.Half.X || offset.Y >= reach.Y + sprite.Vid.Half.Y ||
                MathF.Abs(impact.Z - victim.Z) >= heightReach + sprite.Vid.Object.Size.Z ||
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

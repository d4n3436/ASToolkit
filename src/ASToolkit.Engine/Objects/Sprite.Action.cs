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
    internal int SpriteAction(ActionCode act, int var1, int var2, int var3)
    {
        var code = (ActionCode)((int)act & byte.MaxValue);
        switch (code)
        {
            case ActionCode.GetAmmo or ActionCode.AddAmmo:
                return Owner?.Action(act, var1, var2, var3) ?? 0;
            case ActionCode.Salut:
                if (Animation != SpriteAnimation.Fight)
                {
                    ChangeAnimation(SpriteAnimation.Salut);
                }

                break;
            case ActionCode.Death:
                if (Vid.Damage == 0)
                    break;

                Hp = 0;
                var payload = Vid;
                var impact = Position;
                var point = impact.AsVector2();
                var reach = payload.Half + new Vector2(payload.DeathRange);
                float heightReach = Math.Max(Map.MinBlastHeight, payload.Size.Z);
                bool isScaled = payload.IsExplosive && !payload.DeathRange.IsExactlyZero();
                int damage = payload.Damage;

                for (var sprite = Map.Sprites.FirstHashInBox(point - reach, point + reach); sprite is not null; sprite = Map.Sprites.NextHashInBox())
                {
                    if (sprite == this || sprite.Vid.MaxHp == 0 || payload.ShouldSkipOwnArmy && sprite.Army == Army)
                        continue;

                    var victim = sprite.Position;
                    var offset = Vector2.Abs(victim.AsVector2() - point);
                    float top = victim.Z + sprite.Vid.Size.Z;

                    if (offset.X >= reach.X + sprite.Vid.Half.X || offset.Y >= reach.Y + sprite.Vid.Half.Y ||
                        MathF.Abs(impact.Z - victim.Z) >= heightReach + sprite.Vid.Size.Z ||
                        top < Map.GetGroundZ((victim.AsVector2() + point) * 0.5f) ||
                        top < Map.GetGroundZ((point * 3 + victim.AsVector2()) * 0.25f) ||
                        top < Map.GetGroundZ((victim.AsVector2() * 3 + point) * 0.25f))
                        continue;

                    float distance = victim.AsVector2().NearLength(point);

                    if (isScaled && distance > payload.DeathRange)
                        continue;

                    Map.Action(sprite, ActionCode.Damage, isScaled ? (int)(damage - damage * distance / payload.DeathRange) : damage);
                }

                break;
            case ActionCode.CoorAttack:
                var cursor = new Point(var1, var2);
                var aimer = (ArmedLink ?? this).Vid;
                int cursorY = cursor.Y;
                float cursorZ;

                if (aimer.Weapon.Targets == OldSpriteType.Unit)
                {
                    cursorZ = Map.GetGroundZ(new Vector2(cursor.X, cursor.Y + UnitAimLift)) + UnitAimLift;
                    cursorY += (int)cursorZ;
                }
                else
                {
                    cursorZ = Map.GetGroundZ(new Vector2(cursor.X, cursor.Y)) + GroundAimLift;
                    cursorY += (int)cursorZ - (int)GroundAimLift;
                }

                SetCommand(Command.OneAttack, Map.ConstructGoal(new Vector3(cursor.X, cursorY, cursorZ)));
                break;
            case ActionCode.Random:
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

                break;
            case ActionCode.GetGoal:
                return Map.GetSpriteHandle(Goal);
            case ActionCode.SetGoalCoor:
                Goal = Map.ConstructGoal(new Vector3(var1, var2, var3));
                break;
            case ActionCode.GetBattleRange:
                var armedLink = ArmedLink;

                return (int)(Vid.Weapon.BattleRange.IsExactlyZero() && armedLink is not null ? armedLink.Vid.Weapon.BattleRange : Vid.Weapon.BattleRange);
            case ActionCode.GetLink:
                return Map.GetSpriteHandle(Link);
            case ActionCode.GetUplink:
                return Map.GetSpriteHandle(Owner);
            case ActionCode.GetZSpeed:
                return (int)(VerticalSpeed * ScriptSpeedScale);
            case ActionCode.GetSpeed:
                return (int)(Speed * ScriptSpeedScale);
            case ActionCode.Attack:
                Attack(Map.FindSprite(var1));
                break;
            case ActionCode.Move:
                Move(Map.ConstructGoal(new Vector3(var1, var2, var3)));
                break;
            case ActionCode.MoveTo:
                Move(Map.FindSprite(var1));
                break;
            case ActionCode.BuildUnit:
                var buildOffset = new Point(var2, var3);

                if (var1 == 0)
                {
                    var1 = Map.Action(this, ActionCode.GetItemType, (int)BuiltSpriteType);
                }

                if (var1 <= 0)
                    break;

                var builtVid = Map.FindVid((KnownObject)var1);

                if (builtVid is null)
                    break;

                AddBuiltUnit(builtVid, Map.GetBuildPosition(this, buildOffset));
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
                if (!Speed.IsExactlyZero())
                {
                    Stop();
                }

                int pause = var1 + Map.Random.Next() % (var2 + 1);
                Timer = pause;
                SetCommand(pause != 0 ? Command.Pause : Command.None);
                break;
            case ActionCode.Turn:
                RotateTact(new Angle(unchecked((byte)var1)), Vid.GetVisualFrameDuration(Animation));
                break;
            case ActionCode.ClearCommand:
                SetCommand(Command.None);
                break;
            case ActionCode.FlagmanTrigger:
                var triggerPoint = new Point(var1, var2);

                var trigger = Map.Flagman(Map.ControlledPlayer);

                if (trigger is null || trigger.Position.AsVector2().NearLength(new Vector2(triggerPoint.X, triggerPoint.Y)) >= var3)
                {
                    ActionStack.Insert(new Act((int)ActionCode.FlagmanTrigger, triggerPoint.X, triggerPoint.Y, var3));
                }

                break;
            case ActionCode.ChangeVid:
                var newVid = Map.FindVid((KnownObject)var1);

                if (newVid is null || newVid == Vid)
                    break;

                Map.DropLinks(this);
                IsAwaitingSecondPulse = false;
                Map.Sprites.DeleteFromHash(this);
                RemoveFromUpdates();
                var newAnimation = var2 < 0 ? Animation : (SpriteAnimation)var2;
                var oldDirection = Direction;
                int army = (int)Army & Vid.ArmyMask;
                if (Vid.Counts[army] != 0)
                {
                    Vid.Counts[army]--;
                }

                newVid.LastCreatedAt = Map.RealCurrentTime;
                newVid.Counts[army]++;

                Vid = newVid;

                if (newVid.HasTimers && ExtraData is not null)
                {
                    ExtraData.Lifetime = newVid.Lifetime;
                }
                else if (newVid.HasTimers)
                {
                    CreateExtraData();
                }

                Animation = SpriteAnimation.Stand;
                _direction = default;
                FirstFrame = 0;
                Frame = 0;
                LastFrame = newVid.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
                AddToUpdates();
                Map.Sprites.InsertToHash(this);

                CreateLink();

                ChangeAnimation(newAnimation);
                ChangeDirection(oldDirection);
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
            case ActionCode.ChangeAction:
                ChangeAnimation((SpriteAnimation)var1);
                ChangeDirection(new Angle(unchecked((byte)var2)));
                break;
            case ActionCode.GetAction:
                return (int)Animation;
            case ActionCode.ChangePosition:
                ChangeCoor(new Vector3(var1, var2, Position.Z));
                break;
            case ActionCode.ChangeHeight:
                ChangeCoor(Position with { Z = var1 });
                break;
            case ActionCode.GotoStack:
                ActionStack.Count = var1 + 1;
                ActionStack.Expand(var1 + 1);
                Frame = LastFrame;
                break;
            case ActionCode.BackupCommand:
                ActionStack.Insert(new Act((int)Command << 8 | (int)ActionCode.RestoreCommand, Map.GetSpriteHandle(Goal), 0, 0));
                Goal?.AddRef();
                break;
            case ActionCode.RestoreCommand:
                var restored = Map.FindSprite(var1);
                SetCommand((Command)((int)act >> 8 & byte.MaxValue), restored);

                if (restored is not null)
                {
                    Map.Release(restored);
                }

                break;
            case ActionCode.ClearStack:
                ActionStack.Release();
                break;
            case ActionCode.GetStackCount:
                return ActionStack.Count;
            case ActionCode.GetPercentHp:
                return PercentHp() * 100 / 0xff;
            case ActionCode.CopyStackTo:
                var copy = Map.FindSprite(var1);
                if (copy is null || copy == this)
                    break;

                copy.ClearOrders();
                foreach (var order in ActionStack.Items)
                {
                    if (order.Value == Act.StopMarker)
                        break;

                    copy.ActionStack.Insert(order);
                }

                break;
            case ActionCode.StopStack:
                ActionStack.Insert(new Act((int)code, var1, var2, var3));
                Map.Action(this, ActionCode.NextCommand);
                break;
            case ActionCode.NextCommand:
                if (Animation >= SpriteAnimation.Death)
                    break;

                var graphics = Map.Session.Graphics;
                if ((Owner is not null || MaxSpeed.IsExactlyZero()) && Vid.Properties.HasFlag(ObjectProperties.Wind) && !graphics.WindSpeed.IsExactlyZero())
                {
                    RotateTact(graphics.WindDirection, (int)Math.Max((uint)Map.FrameTime, (uint)Vid.GetVisualFrameDuration(Animation)));
                }

                var state = Animation;

                if (Speed.IsExactlyZero() && state > SpriteAnimation.RRotate && state != SpriteAnimation.StandOpen)
                {
                    ChangeAnimation(SpriteAnimation.Stand);
                }
                else if (!Speed.IsExactlyZero() && state != SpriteAnimation.Go)
                {
                    ChangeAnimation(SpriteAnimation.Go);
                }

                break;
            case ActionCode.ScriptVar:
                Map.ScriptRun(var1, this);
                break;
            case ActionCode.UndoRemove:
                SetFlag(SpriteFlags.Removed, true);
                Map.Sprites.DeleteFromHash(this);
                RemoveFromUpdates();
                return 0;
            case ActionCode.UndoInsert:
                SetFlag(SpriteFlags.Removed, false);
                Map.Sprites.InsertToHash(this);
                AddToUpdates();
                return 0;
            case ActionCode.DestroyUnit:
                var destroyPoint = new Vector2(var2, var3);

                if (var1 <= -1 || Map.FindVid((KnownObject)var1) is null)
                    break;

                var destroyed = Map.GetSpriteScr(SpriteSearch.Id + var1, destroyPoint);

                if (destroyed is not null)
                {
                    Map.DeleteSprite(destroyed);
                }

                break;
            case ActionCode.AddItem:
                InsertItem(var1);
                break;
            case ActionCode.DeleteItem:
                if (ExtraData is not null && ExtraData.Items.DeleteNumber(GetItemNumber(var1)) == 0)
                    return 1;

                break;
            case ActionCode.HaveItem:
                return GetItemNumber(var1) >= 0 ? 1 : 0;
            case ActionCode.DeleteAllItems:
                ExtraData?.Items.Release();
                break;
            case ActionCode.GetItem:
                return var1 >= 0 && var1 < Items.Count ? Items[var1] : 0;
            case ActionCode.GetItemType:
                if (Items.Count == 0)
                    return NoItem;

                var mask = var1 != 0 ? (OldSpriteType)var1 : OldSpriteType.All;
                int matching = 0;
                foreach (int item in Items)
                {
                    matching += (Map.Vid(item).SpriteType & mask) != OldSpriteType.None ? 1 : 0;
                }

                int pick = Map.Random.Next();

                if (matching == 0)
                    return NoItem;

                pick %= matching;
                foreach (int item in Items)
                {
                    if ((Map.Vid(item).SpriteType & mask) != OldSpriteType.None && --pick < 0)
                        return item;
                }

                return NoItem;
            case ActionCode.Damage:
                int maxHp = Vid.GetArmyMaxHp((int)Army);

                if (maxHp <= Hp && var1 < 0)
                    return 1;

                if (Animation >= SpriteAnimation.Death)
                    return 0;

                int handler = Vid.DamageHandler;

                if (handler >= 0 && Map.ScriptRun(handler, this, var2, var1) != 0)
                    return 0;

                if (Vid.MaxHp != 0)
                {
                    ChangeHp(Hp - var1);
                }

                if (maxHp < Hp && var1 < 0)
                {
                    Hp = maxHp;
                }

                if (var1 <= 0)
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
            case ActionCode.Repair:
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
                if (itemCount == 0)
                    break;

                EnsureExtraData();
                ExtraData!.Items.Release();
                foreach (int item in items)
                {
                    ExtraData.Items.Insert(item);
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
                    if (oldAct == OldMoveAct)
                    {
                        oldAct = (int)ActionCode.Move;
                    }
                    else if (oldAct == OldAttackAct)
                    {
                        oldAct = (int)ActionCode.Attack;
                    }
                    else if (oldAct == OldStopStackAct)
                    {
                        oldAct = (int)ActionCode.StopStack;
                    }
                    else
                    {
                        Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Unknown, oldAct, "actionStack.act restore"u8);
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

        if (vid.Properties.HasFlag(ObjectProperties.InvisibleForEnemy))
        {
            SetInvisible(current != Map.ControlledPlayer);
        }

        if (vid.Counts[previous] != 0)
        {
            vid.Counts[previous]--;
        }

        vid.LastCreatedAt = Map.RealCurrentTime;
        vid.Counts[current]++;
    }

    private protected void AddBuiltUnit(Vid vid, Vector3 position)
    {
        var built = Map.CreateSprite(vid, position, Direction);

        if (built is not null)
        {
            Map.Action(this, ActionCode.CopyStackTo, Map.GetSpriteHandle(built));
        }
    }

    private void LoadOrders(ReadOnlySpan<StackAction> stack)
    {
        ActionStack.Release();
        foreach (var action in stack)
        {
            ActionStack.Insert(Act.HasSpriteTarget(action.Code)
                ? new Act(action.Act, Map.GetSpriteHandle(Map.Relation.Decode(action.Var1)), action.Var2, action.Var3)
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

    [EngineRoutine(0x00445be0)]
    private void ClearOrders()
    {
        for (int i = 0; i < ActionStack.Count; i++)
        {
            var order = ActionStack[i];

            if (order.Code != ActionCode.RestoreCommand)
                continue;

            var restored = Map.FindSprite(order.Var1);
            if (restored is not null)
            {
                Map.Release(restored);
            }
        }

        ActionStack.Release();
    }
}

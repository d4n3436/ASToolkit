using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a unit sprite.
/// </summary>
public class Unit : Terrain
{
    private const int AmmoScale = 64;
    private const int IdleScanOdds = 11;
    private const int IntItemsVersion = 11;
    private const int LinkScanOdds = 4;
    private const float WanderReach = 64;
    private const int WordItemsVersion = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="Unit"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Unit(int number, Vid vid, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, vid, position, army, hitPoints, map, direction)
    {
    }

    /// <summary>
    /// Gets or sets the frames the unit has spent turning while stuck.
    /// </summary>
    public int TurnTicks { get; set; }

    /// <summary>
    /// Gets the ammunition the unit holds, in 1/64 rounds.
    /// </summary>
    public int Ammo { get; private set; }

    /// <summary>
    /// Gets the whole rounds the unit holds.
    /// </summary>
    public int Rounds => Ammo / AmmoScale;

    /// <summary>
    /// Gets or sets the behavior bits of the unit.
    /// </summary>
    public WeaponBehavior Behavior { get; set; }

    /// <summary>
    /// Gets a value indicating whether the unit attacks enemies in weapon range.
    /// </summary>
    public bool IsAggressive => Behavior.HasFlag(WeaponBehavior.Aggressive);

    /// <summary>
    /// Gets a value indicating whether the unit moves toward enemies it sees.
    /// </summary>
    public bool IsActive => Behavior.HasFlag(WeaponBehavior.Active);

    /// <summary>
    /// Returns the ammunition the unit holds when full.
    /// </summary>
    /// <returns>The capacity in rounds.</returns>
    [EngineRoutine(0x0043a200)]
    public int GetAmmoCapacity()
    {
        var link = Vid.LinkVid;

        return link is not null && link.CanFight() ? link.AmmoCapacity : Vid.AmmoCapacity;
    }

    /// <summary>
    /// Fills the unit's ammunition.
    /// </summary>
    public void FillAmmo() => Ammo = GetAmmoCapacity() * AmmoScale;

    /// <summary>
    /// Returns the rounds the unit holds for its held weapon.
    /// </summary>
    /// <returns>The rounds.</returns>
    public int GetAmmo() => Rounds;

    /// <summary>
    /// Adds rounds to the unit's held weapon.
    /// </summary>
    /// <param name="amount">The rounds to add, negative to spend.</param>
    /// <returns>The rounds held after the addition.</returns>
    public int AddAmmo(int amount)
    {
        if (GetAmmoCapacity() == Vid.Unlimited)
        {
            Ammo = Vid.Unlimited * AmmoScale;

            return Vid.Unlimited;
        }

        int ammo = Ammo + amount * AmmoScale;
        Ammo = Math.Max(0, ammo);

        if (ShouldRefillAmmo)
        {
            Ammo = GetAmmoCapacity() * AmmoScale;
        }

        return ammo;
    }

    [EngineRoutine(0x00447240)]
    internal override void Destruct()
    {
        Map.DeletePointerToSprite(this);
        base.Destruct();
    }

    [EngineRoutine(0x00447500)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        var code = (ActionCode)act;
        switch (code)
        {
            case ActionCode.Damage:
                return ActionDamage(var1, var2, var3, target);
            case ActionCode.NextCommand:
                NextCommandCase();
                return 0;
            case ActionCode.Repair:
                FillAmmo();

                return base.Action(act, var1, var2, var3, target);
            case ActionCode.GetAmmo:
                return GetAmmo();
            case ActionCode.AddAmmo:
                return AddAmmo(var1);
            case ActionCode.GetBehave:
                return (int)Behavior;
            case ActionCode.SetBehave:
                Behavior = (WeaponBehavior)var1;

                if (Link is not null)
                {
                    Map.Action(Link, code, var1);
                }

                return 0;
        }

        return base.Action(act, var1, var2, var3, target);
    }

    [EngineRoutine(0x00447500)]
    internal override int Action(int act, ref Resource stream, int version)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.Restore:
                base.Action(act, ref stream, version);
                var behavior = Behavior;
                stream.ReadInto(ref behavior);
                Behavior = behavior;
                if (version == IntItemsVersion)
                {
                    int count = 0;
                    stream.ReadInto(ref count);
                    Items.AddRange(stream.Read<int>(count));
                    return 0;
                }

                if (version > WordItemsVersion)
                    return 0;

                int wordCount = 0;
                stream.ReadInto(ref wordCount);
                foreach (short item in stream.Read<short>(wordCount))
                {
                    Items.Add(item);
                }

                return 0;
            case ActionCode.RestoreOld:
                base.Action(act, ref stream, version);
                if (version < OldArmyVersion)
                {
                    byte army = 0;
                    stream.ReadInto(ref army);
                    ChangeArmy(army);
                }

                byte oldBehavior = (byte)Behavior;
                stream.ReadInto(ref oldBehavior);
                Behavior = (WeaponBehavior)((int)Behavior & ~byte.MaxValue | oldBehavior);
                return 0;
            default:
                return base.Action(act, ref stream, version);
        }
    }

    [EngineRoutine(0x00447260)]
    internal override void MoveTact()
    {
        int elapsed = Map.FrameTime;
        int framesPerSecond = Map.FramesPerSecond;
        var next = CalculateNewCoordinates(elapsed);

        if (TurnTicks == 0)
        {
            if (HasGoal && !Speed.IsExactlyZero())
            {
                var goal = DirectionTo(Goal!);

                if (Speed < 0)
                {
                    goal = goal.Opposite;
                }
                goal = RotateForGlide(goal);
                RotateTact(goal, elapsed);

                if (this is { HasReachedX: true, HasReachedY: true })
                {
                    Stop();
                }
            }
        }
        else
        {
            if (Math.Abs(TurnTicks) > framesPerSecond)
            {
                TurnTicks = Math.Sign(TurnTicks) * framesPerSecond / 2;
            }
            var goal = Direction + (TurnTicks > 0 ? Angle.QuarterTurn : -Angle.QuarterTurn);
            RotateTact(goal, elapsed);
            TurnTicks -= Math.Sign(TurnTicks);
        }

        if (next == Position)
            return;

        if (CanPlaceWithCrush(ref next) is null)
        {
            MoveTactMapLimit(next.AsVector2(), elapsed);
            ChangeCoor(next);
        }
        else if (TurnTicks == 0)
        {
            TurnTicks = (Map.Random.Next(2) == 0 ? -1 : 1) * framesPerSecond / 2;
        }
    }

    private protected void AttackSeenEnemy(Sprite attacker)
    {
        var enemy = this.SeekEnemy(Map.Flagman(0));

        if (enemy is not null)
        {
            attacker.SetCommand(Command.OneAttack, enemy);
        }
    }

    private int ActionDamage(int damage, int var2, int var3, Sprite? target)
    {
        int result = base.Action((int)ActionCode.Damage, damage, var2, var3, target);

        if (damage >= 0 && Command == Command.None && !Vid.Speed.IsExactlyZero())
        {
            var wander = Position.AsVector2() + new Angle((byte)Map.Random.Next()).Vector * WanderReach;
            var goal = Map.ConstructGoal(new Vector3(wander, Position.Z));

            if (!SetCommand(Command.Move, goal) && !StartMove())
            {
                SetCommand(Command.None);
            }
        }

        return result;
    }

    private void NextCommandCase()
    {
        if (Animation >= SpriteAnimation.Death)
            return;

        if (Speed.IsExactlyZero())
        {
            ChangeAnimation(SpriteAnimation.Stand);
        }
        else if (Animation != SpriteAnimation.Go)
        {
            ChangeAnimation(SpriteAnimation.Go);
        }

        var link = ArmedLink;
        var result = AttackResult.Pursue;

        if (link is not null && Command is Command.Attack or Command.OneAttack && Goal is not null && link.Goal is null)
        {
            link.SetCommand(Command, Goal);
        }

        if (link is not null || TurnTicks == 0)
        {
            result = TactUnitAttack(link);
        }

        if (result == AttackResult.Pursue && IsActive && Speed.IsExactlyZero())
        {
            StartMove();
        }

        if (result == AttackResult.NoOrder && this is { IsAggressive: true, IsActive: true } ||
            result is AttackResult.Pursue or AttackResult.NoTarget && this is { IsAggressive: true, Command: Command.None or Command.Move or Command.OneAttack } &&
            (LastFrame > FirstFrame || Timer != 0 || Map.Random.Next(IdleScanOdds) == 0))
        {
            AttackSeenEnemy(this);
        }

        if (this is { Command: Command.None, HasGoal: false, Timer: 0 })
        {
            Stop();
        }
    }

    private AttackResult TactUnitAttack(Sprite? link)
    {
        var vid = Vid;
        var result = AttackTact(Math.Max(Map.FrameTime, vid.GetVisualFrameDuration(Animation)));

        switch (result)
        {
            case AttackResult.NoAmmo or AttackResult.GiveUp:
                SetCommand(Command.None);
                break;
            case AttackResult.Approach when vid.Speed.IsExactlyZero():
                if (link is not null && link.HasGoal || HasGoal)
                {
                    SetCommand(Command.None);
                }
                break;
            case AttackResult.Approach when Speed.IsExactlyZero():
                StartMove();
                break;
            case AttackResult.Attacked when !Speed.IsExactlyZero() && (link is null || Goal == link.Goal):
                Stop();
                break;
        }

        if (result == AttackResult.Approach && link is not null && Command is Command.Attack or Command.OneAttack &&
            IsAggressive && (link.Timer != 0 || Map.Random.Next() % LinkScanOdds == 0))
        {
            AttackSeenEnemy(link);
        }

        return result;
    }
}

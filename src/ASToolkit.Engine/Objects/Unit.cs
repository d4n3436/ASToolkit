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
    /// Initializes a new instance of the <see cref="Unit"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x00447130)]
    public Unit(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        TurnTicks = 0;
        Behavior = (ArmedLink ?? this).Vid.Weapon.Behavior;
        FillAmmo();
    }

    /// <summary>
    /// Gets the ammunition the unit holds, in 1/64 rounds.
    /// </summary>
    public int Ammo { get; private set; }

    /// <summary>
    /// Gets the whole rounds the unit holds.
    /// </summary>
    public int Rounds => Ammo / AmmoScale;

    /// <summary>
    /// Gets or sets the frames the unit has spent turning while stuck.
    /// </summary>
    public int TurnTicks { get; set; }

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
        if (Vid.GetMaxAmmo() == Vid.Unlimited)
        {
            Ammo = Vid.Unlimited * AmmoScale;

            return Vid.Unlimited;
        }

        int ammo = Ammo + amount * AmmoScale;
        Ammo = Math.Max(0, ammo);

        if (ShouldRefillAmmo)
        {
            Ammo = Vid.GetMaxAmmo() * AmmoScale;
        }

        return ammo;
    }

    /// <summary>
    /// Fills the unit's ammunition.
    /// </summary>
    public void FillAmmo() => Ammo = Vid.GetMaxAmmo() * AmmoScale;

    [EngineRoutine(0x00447240)]
    internal override void Destruct()
    {
        Map.DeletePointerToSprite(this);
        base.Destruct();
    }

    [EngineRoutine(0x00447260)]
    internal override void MoveTact()
    {
        int elapsed = Map.FrameTime;
        int framesPerSecond = Map.FramesPerSecond;
        var next = MoveTactCalculateCoordinates(elapsed);

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

        if (CanPlaceWithCrushAndGlide(ref next) is null)
        {
            MoveTactMapLimit(next.AsVector2(), elapsed);
            ChangeCoor(next);
        }
        else if (TurnTicks == 0)
        {
            TurnTicks = (Map.Random.Next(2) == 0 ? -1 : 1) * framesPerSecond / 2;
        }
    }

    [EngineRoutine(0x00447500)]
    internal override int Action(ActionCode act, int var1, int var2, int var3)
    {
        switch (act)
        {
            case ActionCode.Damage:
                int damageResult = base.Action(ActionCode.Damage, var1, var2, var3);

                if (var1 >= 0 && Command == Command.None && !Vid.Speed.IsExactlyZero())
                {
                    var wander = Position.AsVector2() + new Angle((byte)Map.Random.Next()).Vector * WanderReach;
                    var goal = Map.ConstructGoal(new Vector3(wander, Position.Z));

                    Move(goal);
                }

                return damageResult;
            case ActionCode.NextCommand:
                if (Animation >= SpriteAnimation.Death)
                    return 0;

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
                    result = AttackTact((int)Math.Max((uint)Map.FrameTime, (uint)Vid.GetVisualFrameDuration(Animation)));

                    if (result is AttackResult.NoAmmo or AttackResult.GiveUp)
                    {
                        SetCommand(Command.None);
                    }
                    else if (result == AttackResult.Approach && MaxSpeed.IsExactlyZero() && (link is not null && link.HasGoal || HasGoal))
                    {
                        SetCommand(Command.None);
                    }
                    else if (result == AttackResult.Approach && !MaxSpeed.IsExactlyZero() && Speed.IsExactlyZero())
                    {
                        StartMove();
                    }
                    else if (result == AttackResult.Attacked && !Speed.IsExactlyZero() && (link is null || Goal == link.Goal))
                    {
                        Stop();
                    }

                    if (result == AttackResult.Approach && link is not null && Command is Command.Attack or Command.OneAttack &&
                        IsAggressive && (link.Timer != 0 || Map.Random.Next() % LinkScanOdds == 0))
                    {
                        AttackSeenEnemy(link);
                    }
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

                return 0;
            case ActionCode.Repair:
                FillAmmo();

                return base.Action(act, var1, var2, var3);
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
                    Map.Action(Link, act, var1);
                }

                return 0;
        }

        return base.Action(act, var1, var2, var3);
    }

    [EngineRoutine(0x00447500)]
    internal override int Action(ActionCode act, ref Resource stream, int version)
    {
        switch (act)
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
                    foreach (int item in stream.Read<int>(count))
                    {
                        InsertItem(item);
                    }
                    return 0;
                }

                if (version > WordItemsVersion)
                    return 0;

                int wordCount = 0;
                stream.ReadInto(ref wordCount);
                foreach (short item in stream.Read<short>(wordCount))
                {
                    InsertItem(item);
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

    private protected void AttackSeenEnemy(Sprite attacker)
    {
        var enemy = SeekEnemy();

        if (enemy is not null)
        {
            attacker.SetCommand(Command.OneAttack, enemy);
        }
    }
}

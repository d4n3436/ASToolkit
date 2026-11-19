using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ASToolkit.Core.Formats;
using Microsoft.Extensions.ObjectPool;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a sprite of the running world with its definition, position, state, orders, links and ammunition.
/// </summary>
public sealed class Unit : IResettable
{
    /// <summary>
    /// Represents the object id of the torso for weapon slot 0.
    /// </summary>
    public const int WeaponIdBase = 10;
    
    /// <summary>
    /// Represents the number of weapon slots.
    /// </summary>
    public const int WeaponSlots = 10;
    
    /// <summary>
    /// Represents the slot of the pistols.
    /// </summary>
    public const int Pistols = 1;
    
    /// <summary>
    /// Represents the item of weapon slot 0.
    /// </summary>
    public const int WeaponItemBase = 260;
    
    private const int ReloadTimerOffset = 5000;
    private const int AmmoScale = 64;
    private const int MaxWeaponSlot = 9;
    private const int SpawnScatter = 8;
    private const float VelocityScale = 1e6f;
    private const float ReachTolerance = 0.5f;
    
    private int[]? _slotAmmo;
    private bool _secondChildPulse;
    private Angle _direction;
    private Unit? _owner;
    private Unit? _link;
    private Unit? _goal;
    private Unit? _shooter;

    /// <summary>
    /// Creates an empty unit. Used for object pooled initialization.
    /// </summary>
    public Unit()
    {
        Definition = null!;
    }

    /// <summary>
    /// Creates a unit of a definition.
    /// </summary>
    /// <param name="number">The unit number.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="army">The army.</param>
    /// <param name="hp">The health.</param>
    public Unit(int number, SpriteDefinition definition, Vector3 position, Army army, int hp)
    {
        Initialize(number, definition, position, army, hp);
    }

    /// <summary>
    /// Gets the unit number.
    /// </summary>
    public int Number { get; private set; }

    /// <summary>
    /// Gets or sets the handle the level script refers to the unit by, 0 when it has none.
    /// </summary>
    public int Handle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit counts toward its definition's per-army count.
    /// </summary>
    public bool IsCounted { get; set; }

    /// <summary>
    /// Gets or sets the side list that removes the unit when it leaves.
    /// </summary>
    public UpdateRole Role { get; set; }

    /// <summary>
    /// Gets the definition of the unit.
    /// </summary>
    public SpriteDefinition Definition { get; private set; }

    /// <summary>
    /// Gets or sets the army of the unit.
    /// </summary>
    public Army Army { get; set; }

    /// <summary>
    /// Gets or sets the map sprite the unit was built from.
    /// </summary>
    public MergedMapSprite? Placement { get; set; }

    /// <summary>
    /// Gets or sets the position of the unit.
    /// </summary>
    public Vector3 Position { get; set; }

    internal CollisionVolume CollisionVolume => Definition.GetCollisionVolume(Position);

    /// <summary>
    /// Gets or sets the speed of the unit in pixels per millisecond.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Gets or sets the width and height of a region sprite.
    /// </summary>
    public Vector2 RegionSize { get; set; }

    /// <summary>
    /// Gets or sets the frames the unit has spent turning while stuck.
    /// </summary>
    public int TurnTicks { get; set; }

    /// <summary>
    /// Gets or sets the fraction of a turn step carried to the next frame.
    /// </summary>
    public float TurnCarry { get; set; }

    /// <summary>
    /// Gets or sets the command the unit carries out.
    /// </summary>
    public UnitCommand Command { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit moves toward its goal.
    /// </summary>
    public bool IsMoving { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit is leaving the world.
    /// </summary>
    public bool LeavesEffects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit has passed its goal's x coordinate.
    /// </summary>
    public bool HasReachedX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit has passed its goal's y coordinate.
    /// </summary>
    public bool HasReachedY { get; set; }

    /// <summary>
    /// Gets or sets the health of the unit.
    /// </summary>
    public int Hp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit is hidden.
    /// </summary>
    public bool IsInvisible { get; set; }

    /// <summary>
    /// Gets the items the unit carries.
    /// </summary>
    public List<int> Items { get; } = [];

    /// <summary>
    /// Gets or sets the grid bucket the unit is filed in.
    /// </summary>
    public int Bucket { get; set; }

    /// <summary>
    /// Gets or sets the unit's index in its grid bucket.
    /// </summary>
    public int Slot { get; set; }

    /// <summary>
    /// Gets or sets the unit's index in the grid's unit list.
    /// </summary>
    public int ListIndex { get; set; }

    /// <summary>
    /// Gets the order stack of the unit.
    /// </summary>
    public OrderStack Orders { get; } = new();

    /// <summary>
    /// Gets or sets the time the unit died, negative while it lives.
    /// </summary>
    public int DiedAt { get; set; }

    /// <summary>
    /// Gets the direction the unit faces.
    /// </summary>
    public Angle Direction { get => _direction; init => _direction = value; }

    /// <summary>
    /// Gets the state of the unit.
    /// </summary>
    public SpriteAnimation Animation { get; private set; }

    /// <summary>
    /// Gets the first frame of the state.
    /// </summary>
    public int FirstFrame { get; private set; }

    /// <summary>
    /// Gets or sets the current frame.
    /// </summary>
    public int Frame { get; set; }

    /// <summary>
    /// Gets the last frame of the state.
    /// </summary>
    public int LastFrame { get; private set; }

    /// <summary>
    /// Gets or sets the time of the unit's last frame step.
    /// </summary>
    public int LastTickTime { get; set; }

    /// <summary>
    /// Gets or sets the time the unit was created.
    /// </summary>
    public int CreatedAt
    {
        get;
        set
        {
            field = value;
            KeyframeStart = value;
            KeyframePosition = 0;
        }
    }

    /// <summary>
    /// Gets or sets the time the unit's keyframe track started.
    /// </summary>
    public int KeyframeStart { get; set; }

    /// <summary>
    /// Gets or sets the position along the unit's keyframe track.
    /// </summary>
    public float KeyframePosition { get; set; }

    /// <summary>
    /// Gets or sets the time the unit has left before it dies.
    /// </summary>
    public int Lifetime { get; set; }

    /// <summary>
    /// Gets or sets the unit's countdown timer in milliseconds.
    /// </summary>
    public int Timer { get; set; }

    /// <summary>
    /// Gets the ammunition the unit holds, in 1/64 rounds.
    /// </summary>
    public int Ammo { get; private set; }

    /// <summary>
    /// Gets the whole rounds the unit holds.
    /// </summary>
    public int Rounds => Ammo / AmmoScale;

    /// <summary>
    /// Gets or sets a value indicating whether the unit refills its ammunition.
    /// </summary>
    public bool RefillsAmmo { get; set; }

    /// <summary>
    /// Gets the weapon slot of the torso the unit's definition links.
    /// </summary>
    public int WeaponSlot => (int)(Definition.LinkDefinition?.Id ?? 0) - WeaponIdBase;

    /// <summary>
    /// Gets the ammunition the unit keeps for the slots it does not hold.
    /// </summary>
    public int[] SlotAmmo => _slotAmmo ??= new int[MaxWeaponSlot + 1];

    /// <summary>
    /// Gets or sets the health the unit had before the hit that killed it.
    /// </summary>
    public int HpBeforeDeath { get; set; }

    /// <summary>
    /// Gets or sets the behavior bits of the unit.
    /// </summary>
    public WeaponBehavior Behavior { get; set; }

    /// <summary>
    /// Gets or sets the period of the unit's stream children.
    /// </summary>
    public int StreamPeriod { get; set; }

    /// <summary>
    /// Gets a value indicating whether the unit attacks enemies in weapon range.
    /// </summary>
    public bool IsAggressive => Behavior.HasFlag(WeaponBehavior.Aggressive);

    /// <summary>
    /// Gets a value indicating whether the unit moves toward enemies it sees.
    /// </summary>
    public bool IsActive => Behavior.HasFlag(WeaponBehavior.Active);

    /// <summary>
    /// Gets or sets the vertical speed of the unit.
    /// </summary>
    public float VerticalSpeed { get; set; }

    /// <summary>
    /// Gets or sets the top speed of the unit in pixels per millisecond.
    /// </summary>
    public float MaxSpeed { get; set; }

    /// <summary>
    /// Gets or sets the sprite the unit moves toward or attacks.
    /// </summary>
    public Unit? Goal { get => _goal; set => _goal = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets the point the unit moves toward.
    /// </summary>
    public Vector3? GoalPoint { get; set; }

    /// <summary>
    /// Gets or sets the sprite that fired the unit.
    /// </summary>
    public Unit? Shooter { get => _shooter; set => _shooter = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets the sprite the unit is linked to.
    /// </summary>
    public Unit? Owner { get => _owner; set => _owner = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets the unit's offset from its owner.
    /// </summary>
    public Vector3 OwnerOffset { get; set; }

    /// <summary>
    /// Gets or sets the next sprite of the unit's link chain.
    /// </summary>
    public Unit? Link { get => _link; set => _link = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets how many child launches deep the unit was created.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit's last move was blocked.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit climbs onto higher ground.
    /// </summary>
    public bool IsClimbing { get; set; }

    /// <summary>
    /// Gets a value indicating whether the unit has a goal sprite or point.
    /// </summary>
    public bool HasGoal => Goal is not null || GoalPoint is not null;

    /// <summary>
    /// Gets the position of the unit's goal.
    /// </summary>
    public Vector3 GoalPosition => Goal?.Position ?? GoalPoint.GetValueOrDefault();

    /// <summary>
    /// Gets a value indicating whether the unit has health left.
    /// </summary>
    public bool IsAlive => Hp > 0;

    /// <summary>
    /// Gets a value indicating whether another sprite refers to the unit.
    /// </summary>
    public bool IsReferenced { get; private set; }

    /// <summary>
    /// Changes the unit's definition and state.
    /// </summary>
    /// <param name="definition">The new definition.</param>
    /// <param name="state">The state to enter.</param>
    [EngineRoutine(0x00440830)]
    public void ChangeVid(SpriteDefinition definition, SpriteAnimation state)
    {
        var direction = Direction;
        if (IsCounted)
        {
            int army = (int)Army & 3;
            if (Definition.Counts[army] != 0)
            {
                Definition.Counts[army]--;
            }

            definition.Counts[army]++;
        }

        Definition = definition;
        ChangeAnimation(state);
        SetDirection(direction);
    }

    /// <summary>
    /// Resets the unit to a new sprite of a definition.
    /// </summary>
    /// <param name="number">The unit number.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="army">The army.</param>
    /// <param name="hp">The health.</param>
    /// <param name="direction">The direction.</param>
    [MemberNotNull(nameof(Definition))]
    public void Initialize(int number, SpriteDefinition definition, Vector3 position, Army army, int hp, Angle direction = default)
    {
        _secondChildPulse = false;
        StreamPeriod = 1;
        _direction = direction;
        _owner = null;
        _link = null;
        _goal = null;
        _shooter = null;
        Number = number;
        Handle = 0;
        IsCounted = false;
        Role = UpdateRole.None;
        Definition = definition;
        Behavior = definition.Behavior;
        Army = army;
        Placement = null;
        Position = position;
        Speed = 0;
        RegionSize = Vector2.Zero;
        TurnTicks = 0;
        TurnCarry = 0;
        Command = default;
        IsMoving = false;
        HasReachedX = false;
        HasReachedY = false;
        Hp = hp;
        IsInvisible = false;
        Items.Clear();
        Bucket = -1;
        Slot = -1;
        ListIndex = -1;
        Orders.Clear();
        DiedAt = -1;
        Animation = default;
        FirstFrame = definition.Layout.GetFirstFrame(SpriteAnimation.Stand);
        Frame = FirstFrame;
        LastFrame = FirstFrame + definition.Layout.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
        LastTickTime = 0;
        CreatedAt = -1;
        Lifetime = definition.Lifetime;
        Timer = 0;
        _slotAmmo = null;
        RefillsAmmo = false;
        FillAmmo();
        VerticalSpeed = 0;
        MaxSpeed = definition.Speed;
        GoalPoint = null;
        OwnerOffset = Vector3.Zero;
        Depth = 0;
        IsBlocked = false;
        IsClimbing = false;
        IsReferenced = false;
    }
    
    /// <summary>
    /// Gets the armed object the unit's definition links, when it is the unit's link.
    /// </summary>
    public Unit? ArmedLink
    {
        get
        {
            var link = Link;
            return link is not null && link.Definition.Id == Definition.Object.Link && link.Definition.Object.WeaponIndex != 0 ? link : null;
        }
    }

    /// <summary>
    /// Marks the unit as referred to by another sprite.
    /// </summary>
    public void MarkReferenced() => IsReferenced = true;

    private static Unit? MarkReferenced(Unit? unit)
    {
        unit?.MarkReferenced();

        return unit;
    }

    /// <summary>
    /// Starts the unit in a state and draws its starting frame.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="time">The creation time.</param>
    /// <param name="random">The random source.</param>
    [EngineRoutine(0x0043f200)]
    public void Start(SpriteAnimation state, Angle direction, int time, Random random)
    {
        if (Definition.Object.Properties.HasFlag(ObjectProperties.Noise))
        {
            float x = Position.X + SpawnScatter - random.Next() % (SpawnScatter * 2 + 1);
            float y = Position.Y + SpawnScatter - random.Next() % (SpawnScatter * 2 + 1);
            Position = Position with { X = x, Y = y };
        }

        var layout = Definition.Layout;
        bool firstFrame = Definition.Object.Properties.HasFlag(ObjectProperties.OnePhase);
        LastTickTime = time - (firstFrame ? 0 : random.Next(Definition.SpriteSpeed + 1));
        _direction = default;
        Animation = state;
        FirstFrame = layout.GetFirstFrame(state);
        Frame = FirstFrame;
        LastFrame = layout.GetFramesPerDirection(state) - 1;
        SetDirection(direction);

        if (state == SpriteAnimation.Stand && LastFrame - FirstFrame > 0 && !firstFrame)
        {
            Frame += random.Next(LastFrame - FirstFrame + 1);
        }
    }

    /// <summary>
    /// Changes the unit's state, taking its links into death with it.
    /// </summary>
    /// <param name="state">The state.</param>
    [EngineRoutine(0x00442ee0)]
    public void ChangeAnimation(SpriteAnimation state)
    {
        var link = _link;

        if (link is not null && link.Definition.Id == Definition.Object.Link && (state >= SpriteAnimation.Death || link.Definition.Object.WeaponIndex == 0 &&
            !link.Definition.Object.Properties.HasFlag(ObjectProperties.Wind) && link.Animation is not SpriteAnimation.Fight and < SpriteAnimation.Death))
        {
            link.ChangeAnimation(state);
        }

        if (Animation == state)
            return;

        var layout = Definition.Layout;
        var direction = GetShownDirection(_direction);

        FirstFrame = layout.GetFirstFrame(state) + direction.GetDirectionIndex(layout.Directions) * layout.GetFramesPerDirection(state);
        Frame = FirstFrame;
        Animation = state;
        LastFrame = state > SpriteAnimation.Unload && layout.GetFrameCount(state) == 0
            ? FirstFrame : FirstFrame + layout.GetFramesPerDirection(state) - 1;
    }

    /// <summary>
    /// Turns the unit, turning links that keep their owner's direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    [EngineRoutine(0x00442be0)]
    public void SetDirection(Angle direction)
    {
        var shown = GetShownDirection(direction);

        if (_direction == shown)
            return;

        var link = _link;

        if (link is not null && link.Definition.Id == Definition.Object.Link && link.Definition.Object.RotationPeriod.IsNear(SpriteDefinition.Unlimited))
        {
            link.SetDirection(direction);
        }

        var layout = Definition.Layout;

        if (layout.Directions != 1)
        {
            int offset = Frame - FirstFrame;
            FirstFrame = layout.GetFirstFrame(Animation) + shown.GetDirectionIndex(layout.Directions) * layout.GetFramesPerDirection(Animation);
            LastFrame = FirstFrame + layout.GetFramesPerDirection(Animation) - 1;
            Frame = FirstFrame + offset;
        }

        _direction = direction;
    }

    // Falling motion-tracking objects show the direction of their screen velocity
    private Angle GetShownDirection(Angle direction)
    {
        if (VerticalSpeed.IsZero() || !Definition.Object.Properties.HasFlag(ObjectProperties.VerticalDirection))
            return direction;

        var vector = direction.Vector;

        return Angle.FromOffset(new Vector2(vector.X * Speed * VelocityScale, -(vector.Y * Speed + VerticalSpeed) * VelocityScale));
    }

    /// <summary>
    /// Records whether a step passed the goal on either axis.
    /// </summary>
    /// <param name="from">The position before the step.</param>
    /// <param name="next">The position after the step.</param>
    /// <param name="goal">The goal point.</param>
    public void UpdateReach(Vector3 from, Vector3 next, Vector2 goal)
    {
        HasReachedX |= Math.Min(from.X, next.X) - ReachTolerance <= goal.X && goal.X <= Math.Max(from.X, next.X) + ReachTolerance;
        HasReachedY |= Math.Min(from.Y, next.Y) - ReachTolerance <= goal.Y && goal.Y <= Math.Max(from.Y, next.Y) + ReachTolerance;
    }

    /// <summary>
    /// Stops the unit's move toward its goal.
    /// </summary>
    public void ClearMove()
    {
        IsMoving = false;
        HasReachedX = false;
        HasReachedY = false;
    }

    /// <summary>
    /// Starts the unit's move toward its goal.
    /// </summary>
    /// <param name="maxSpeed">The speed the unit reaches at once when its definition has no acceleration.</param>
    [EngineRoutine(0x004443f0)]
    public void StartMoving(float maxSpeed)
    {
        IsMoving = true;
        HasReachedX = false;
        HasReachedY = false;

        if (Animation < SpriteAnimation.Death && Animation != SpriteAnimation.StartMove)
        {
            ChangeAnimation(Speed.IsZero() ? SpriteAnimation.StartMove : SpriteAnimation.Go);
        }

        if (Definition.Object.Acceleration.IsNear(SpriteDefinition.Unlimited))
        {
            Speed = maxSpeed;
        }
    }

    /// <summary>
    /// Returns the ammunition the unit holds when full.
    /// </summary>
    /// <returns>The capacity in rounds.</returns>
    [EngineRoutine(0x0043a200)]
    public int GetAmmoCapacity()
    {
        var link = Definition.LinkDefinition;

        return link is not null && link.FightChild != 0 && link.Object.WeaponIndex != 0 ? link.AmmoCapacity : Definition.AmmoCapacity;
    }

    /// <summary>
    /// Fills the unit's ammunition.
    /// </summary>
    public void FillAmmo() => Ammo = GetAmmoCapacity() * AmmoScale;
    
    /// <summary>
    /// Returns the rounds the unit holds for a weapon slot.
    /// </summary>
    /// <param name="slot">The slot, or 0 for the held weapon.</param>
    /// <returns>The rounds.</returns>
    [EngineRoutine(0x00454800)]
    [EngineRoutine(0x00440830)]
    public int GetAmmo(int slot = 0)
    {
        if (Definition.Object.SpriteClass == SpriteClass.Man)
            return slot != 0 && slot != WeaponSlot ? SlotAmmo[slot] : GetOwnAmmo();

        return Owner?.GetAmmo(slot) ?? GetOwnAmmo();
    }

    /// <summary>
    /// Adds rounds to a weapon slot and returns the rounds held after.
    /// </summary>
    /// <param name="amount">The rounds to add, negative to spend.</param>
    /// <param name="slot">The slot, or 0 for the held weapon.</param>
    /// <returns>The rounds held.</returns>
    [EngineRoutine(0x00454800)]
    [EngineRoutine(0x00440830)]
    public int AddAmmo(int amount, int slot = 0)
    {
        if (Definition.Object.SpriteClass == SpriteClass.Man)
        {
            if (slot > MaxWeaponSlot)
                return 0;

            if (slot != 0 && slot != WeaponSlot)
                return SlotAmmo[slot] += amount;

            return AddOwnAmmo(amount);
        }

        return Owner?.AddAmmo(amount, slot) ?? AddOwnAmmo(amount);
    }

    [EngineRoutine(0x00447500)]
    private int GetOwnAmmo() => Ammo / AmmoScale;

    [EngineRoutine(0x00447500)]
    private int AddOwnAmmo(int amount)
    {
        if (GetAmmoCapacity() == SpriteDefinition.Unlimited)
        {
            Ammo = SpriteDefinition.Unlimited * AmmoScale;

            return SpriteDefinition.Unlimited;
        }

        int ammo = Ammo + amount * AmmoScale;
        Ammo = Math.Max(0, ammo);

        if (RefillsAmmo)
        {
            Ammo = GetAmmoCapacity() * AmmoScale;
        }

        return ammo;
    }

    [EngineRoutine(0x004460b0)]
    internal AttackResult TryAttack(int elapsed)
    {
        var link = ArmedLink;

        if (link is not null)
        {
            var result = link.TryAttack(elapsed);

            if (result != AttackResult.NoTarget)
                return result;

            return HasGoal ? AttackResult.NoOrder : AttackResult.NoTarget;
        }

        if (!Definition.IsArmed || Definition.FightChild == 0)
            return AttackResult.Unarmed;

        if (!HasGoal)
        {
            FaceOwner(elapsed, Timer == 0);

            return AttackResult.NoTarget;
        }

        if (IsReloading)
        {
            Aim(elapsed);

            return AttackResult.Busy;
        }

        if (Command is not (UnitCommand.Attack or UnitCommand.OneAttack or UnitCommand.DistAttack))
        {
            FaceOwner(elapsed, Timer == 0);

            return AttackResult.NoOrder;
        }

        float distance = Position.AsVector2().MeasureOctagonalDistance(GoalPosition.AsVector2());
        bool inRange = Owner is null || Command == UnitCommand.DistAttack || distance < Definition.BattleRange && distance > Definition.MinRange;
        bool aimed = inRange && (Aim(elapsed) == 0 || Definition.AttacksWhileTurning);

        if (!inRange)
        {
            FaceOwner(elapsed, true);
        }

        if (distance <= Definition.BattleRange)
        {
            if (!aimed)
                return AttackResult.Busy;

            int cost = Math.Abs(Definition.ChildCounts[(int)SpriteAnimation.Fight]);

            if (GetAmmo() < cost)
                return AttackResult.NoAmmo;

            AddAmmo(-cost);

            ChangeAnimation(SpriteAnimation.Fight);
            Timer = Definition.ReloadTime + ReloadTimerOffset;

            return AttackResult.Attacked;
        }

        if (Command == UnitCommand.Attack)
            return AttackResult.Approach;

        return distance < Definition.DetectRange * 2 ? AttackResult.Pursue : AttackResult.GiveUp;
    }

    private void FaceOwner(int elapsed, bool idle)
    {
        if (Owner is not null && idle)
        {
            SetDirection(Direction.TurnToward(Owner.Direction, Definition.Object.RotationPeriod, elapsed, out _));
        }
    }

    /// <summary>
    /// Gets a value indicating whether the unit is between attacks.
    /// </summary>
    public bool IsReloading => Timer > ReloadTimerOffset || Animation == SpriteAnimation.Fight && Frame <= LastFrame;

    [EngineRoutine(0x004463a0)]
    private int Aim(int elapsed)
    {
        var direction = Angle.Aim((GoalPosition - Position).AsVector2());
        SetDirection(Direction.TurnToward(direction, Definition.Object.RotationPeriod, elapsed, out int remaining));

        return remaining;
    }

    /// <summary>
    /// Returns which of a mirrored pair of children the next launch takes, alternating for definitions that attack twice.
    /// </summary>
    /// <param name="animation">The state whose children launch.</param>
    /// <param name="owner">The definition whose children launch, or null for the unit's definition.</param>
    /// <returns><see langword="true"/> for the second of the pair; otherwise, <see langword="false"/>.</returns>
    public bool TakeChildPulse(SpriteAnimation animation, SpriteDefinition? owner = null)
    {
        owner ??= Definition;
        bool second = _secondChildPulse;

        if (owner.AttacksTwice && owner.Children[(int)animation] != 0 && Math.Abs(owner.ChildCounts[(int)animation]) == ChildEmission.MirroredPairCount)
        {
            _secondChildPulse = !second;
        }

        return second;
    }

    /// <summary>
    /// Returns the direction the unit turns to toward a goal direction during a frame.
    /// </summary>
    /// <param name="goal">The goal direction.</param>
    /// <param name="elapsed">The frame's length in milliseconds.</param>
    /// <returns>The new direction.</returns>
    public Angle TurnWhileMoving(Angle goal, int elapsed)
    {
        float period = Definition.Object.RotationPeriod;

        if (elapsed >= GameClock.DefaultFrameTime || Direction == goal || period.IsZero() || period.IsNear(SpriteDefinition.Unlimited))
        {
            TurnCarry = 0;

            return Direction.TurnToward(goal, period, elapsed, out _);
        }

        // Carry the unrounded turn to the next frame above 60 frames a second
        float turn = elapsed * (Angle.FullTurn / period) + TurnCarry;
        int step = (int)turn;
        TurnCarry = turn - step;

        return Direction.StepToward(goal, step, out _);
    }

    /// <summary>
    /// Traverses over the unit's <see cref="Link"/> chain.
    /// </summary>
    /// <returns>The chain as an <see cref="IEnumerable{T}"/>.</returns>
    public IEnumerable<Unit> EnumerateLinks()
    {
        for (var link = Link; link is not null; link = link.Link)
        {
            yield return link;
        }
    }

    bool IResettable.TryReset() => !IsReferenced;
}

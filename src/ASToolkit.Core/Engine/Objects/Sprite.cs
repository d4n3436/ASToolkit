using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ASToolkit.Core.Formats;
using Microsoft.Extensions.ObjectPool;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a sprite of the running map with its definition, position, state, orders, links and ammunition.
/// </summary>
public sealed class Sprite : IResettable
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
    private Sprite? _owner;
    private Sprite? _link;
    private Sprite? _goal;
    private Sprite? _shooter;
    private Sprite? _linkerBase;

    /// <summary>
    /// Creates an empty sprite. Used for object pooled initialization.
    /// </summary>
    public Sprite()
    {
        Definition = null!;
    }

    /// <summary>
    /// Creates a sprite from a definition.
    /// </summary>
    /// <param name="number">The sprite number.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="army">The army.</param>
    /// <param name="hp">The health.</param>
    public Sprite(int number, SpriteDefinition definition, Vector3 position, Army army, int hp)
    {
        Initialize(number, definition, position, army, hp);
    }

    /// <summary>
    /// Gets the sprite number.
    /// </summary>
    public int Number { get; private set; }

    /// <summary>
    /// Gets or sets the handle the level script refers to the sprite by, 0 when it has none.
    /// </summary>
    public int Handle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite counts toward its definition's per-army count.
    /// </summary>
    public bool IsCounted { get; set; }

    /// <summary>
    /// Gets or sets the side list that removes the sprite when it leaves.
    /// </summary>
    public UpdateRole Role { get; set; }

    /// <summary>
    /// Gets the definition of the sprite.
    /// </summary>
    public SpriteDefinition Definition { get; private set; }

    /// <summary>
    /// Gets or sets the army of the sprite.
    /// </summary>
    public Army Army { get; set; }

    /// <summary>
    /// Gets or sets the map sprite the sprite was built from.
    /// </summary>
    public MergedMapSprite? Placement { get; set; }

    /// <summary>
    /// Gets or sets the position of the sprite.
    /// </summary>
    public Vector3 Position { get; set; }

    internal CollisionVolume CollisionVolume => Definition.GetCollisionVolume(Position);

    /// <summary>
    /// Gets or sets the speed of the sprite in pixels per millisecond.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Gets or sets the width and height of a region sprite.
    /// </summary>
    public Vector2 RegionSize { get; set; }

    /// <summary>
    /// Gets or sets the frames the sprite has spent turning while stuck.
    /// </summary>
    public int TurnTicks { get; set; }

    /// <summary>
    /// Gets or sets the fraction of a turn step carried to the next frame.
    /// </summary>
    public float TurnCarry { get; set; }

    /// <summary>
    /// Gets or sets the command the sprite carries out.
    /// </summary>
    public Command Command { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite moves toward its goal.
    /// </summary>
    public bool IsMoving { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is leaving the map.
    /// </summary>
    public bool LeavesEffects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite has passed its goal's x coordinate.
    /// </summary>
    public bool HasReachedX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite has passed its goal's y coordinate.
    /// </summary>
    public bool HasReachedY { get; set; }

    /// <summary>
    /// Gets or sets the health of the sprite.
    /// </summary>
    public int Hp { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is hidden.
    /// </summary>
    public bool IsInvisible { get; set; }

    /// <summary>
    /// Gets the items the sprite carries.
    /// </summary>
    public List<int> Items { get; } = [];

    /// <summary>
    /// Gets or sets the hash bucket the sprite is filed in.
    /// </summary>
    public int Bucket { get; set; }

    /// <summary>
    /// Gets or sets the sprite's index in its hash bucket.
    /// </summary>
    public int Slot { get; set; }

    internal SpriteCollector? Collector { get; set; }

    /// <summary>
    /// Gets or sets the sprite's index in the collector's unit list.
    /// </summary>
    public int ListIndex { get; set; }

    /// <summary>
    /// Gets the order stack of the sprite.
    /// </summary>
    public ActionStack ActionStack { get; } = new();

    /// <summary>
    /// Gets or sets the time the sprite died, negative while it lives.
    /// </summary>
    public int DiedAt { get; set; }

    /// <summary>
    /// Gets the direction the sprite faces.
    /// </summary>
    public Angle Direction { get => _direction; init => _direction = value; }

    /// <summary>
    /// Gets the state of the sprite.
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
    /// Gets or sets the time of the sprite's last frame step.
    /// </summary>
    public int LastTickTime { get; set; }

    /// <summary>
    /// Gets or sets the time the sprite was created.
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
    /// Gets or sets the time the sprite's keyframe track started.
    /// </summary>
    public int KeyframeStart { get; set; }

    /// <summary>
    /// Gets or sets the position along the sprite's keyframe track.
    /// </summary>
    public float KeyframePosition { get; set; }

    /// <summary>
    /// Gets or sets the time the sprite has left before it dies.
    /// </summary>
    public int Lifetime { get; set; }

    /// <summary>
    /// Gets or sets the sprite's countdown timer in milliseconds.
    /// </summary>
    public int Timer { get; set; }

    /// <summary>
    /// Gets the ammunition the sprite holds, in 1/64 rounds.
    /// </summary>
    public int Ammo { get; private set; }

    /// <summary>
    /// Gets the whole rounds the sprite holds.
    /// </summary>
    public int Rounds => Ammo / AmmoScale;

    /// <summary>
    /// Gets or sets a value indicating whether the sprite refills its ammunition.
    /// </summary>
    public bool RefillsAmmo { get; set; }

    /// <summary>
    /// Gets the weapon slot of the torso the sprite's definition links.
    /// </summary>
    public int WeaponSlot => (int)(Definition.LinkDefinition?.Id ?? 0) - WeaponIdBase;

    /// <summary>
    /// Gets the ammunition the sprite keeps for the slots it does not hold.
    /// </summary>
    public int[] SlotAmmo => _slotAmmo ??= new int[MaxWeaponSlot + 1];

    /// <summary>
    /// Gets or sets the behavior bits of the sprite.
    /// </summary>
    public WeaponBehavior Behavior { get; set; }

    /// <summary>
    /// Gets or sets the period of the sprite's stream children.
    /// </summary>
    public int StreamPeriod { get; set; }

    /// <summary>
    /// Gets a value indicating whether the sprite attacks enemies in weapon range.
    /// </summary>
    public bool IsAggressive => Behavior.HasFlag(WeaponBehavior.Aggressive);

    /// <summary>
    /// Gets a value indicating whether the sprite moves toward enemies it sees.
    /// </summary>
    public bool IsActive => Behavior.HasFlag(WeaponBehavior.Active);

    /// <summary>
    /// Gets or sets the vertical speed of the sprite.
    /// </summary>
    public float VerticalSpeed { get; set; }

    /// <summary>
    /// Gets or sets the top speed of the sprite in pixels per millisecond.
    /// </summary>
    public float MaxSpeed { get; set; }

    /// <summary>
    /// Gets or sets the sprite the sprite moves toward or attacks.
    /// </summary>
    public Sprite? Goal { get => _goal; set => _goal = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets the point the sprite moves toward.
    /// </summary>
    public Vector3? GoalPoint { get; set; }

    /// <summary>
    /// Gets or sets the sprite that fired the sprite.
    /// </summary>
    public Sprite? Shooter { get => _shooter; set => _shooter = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets the sprite the sprite is linked to.
    /// </summary>
    public Sprite? Owner { get => _owner; set => _owner = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets the sprite a linker turns around, the parent it was created with.
    /// </summary>
    public Sprite? LinkerBase { get => _linkerBase; set => _linkerBase = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets a linker's offset from its base when it was created.
    /// </summary>
    public Vector3 LinkerOffset { get; set; }

    /// <summary>
    /// Gets or sets the direction a linker was created facing.
    /// </summary>
    public Angle LinkerDirection { get; set; }

    /// <summary>
    /// Gets or sets the next sprite of the sprite's link chain.
    /// </summary>
    public Sprite? Link { get => _link; set => _link = MarkReferenced(value); }

    /// <summary>
    /// Gets or sets how many child launches deep the sprite was created.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite's last move was blocked.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite climbs onto higher ground.
    /// </summary>
    public bool IsClimbing { get; set; }

    /// <summary>
    /// Gets a value indicating whether the sprite has a goal sprite or point.
    /// </summary>
    public bool HasGoal => Goal is not null || GoalPoint is not null;

    /// <summary>
    /// Gets the position of the sprite's goal.
    /// </summary>
    public Vector3 GoalPosition => Goal?.Position ?? GoalPoint.GetValueOrDefault();

    /// <summary>
    /// Gets a value indicating whether the sprite has health left and has not entered a death state.
    /// </summary>
    public bool IsAlive => Hp > 0 && Animation < SpriteAnimation.Death;

    /// <summary>
    /// Gets a value indicating whether another sprite refers to the sprite.
    /// </summary>
    public bool IsReferenced { get; private set; }

    /// <summary>
    /// Changes the sprite's definition, moving its count and resetting it to the first frame of the standing state, facing up.
    /// </summary>
    /// <param name="definition">The new definition.</param>
    public void ChangeVid(SpriteDefinition definition)
    {
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
        Animation = SpriteAnimation.Stand;
        _direction = default;
        FirstFrame = 0;
        Frame = 0;
        LastFrame = definition.Layout.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
    }

    /// <summary>
    /// Reinitializes the sprite with a definition.
    /// </summary>
    /// <param name="number">The sprite number.</param>
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
        _linkerBase = null;
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
        Collector = null;
        ListIndex = -1;
        ActionStack.Clear();
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
        LinkerOffset = Vector3.Zero;
        LinkerDirection = default;
        Depth = 0;
        IsBlocked = false;
        IsClimbing = false;
        IsReferenced = false;
    }
    
    /// <summary>
    /// Gets the armed object the sprite's definition links, when it is the sprite's link.
    /// </summary>
    public Sprite? ArmedLink
    {
        get
        {
            var link = Link;
            return link is not null && link.Definition == Definition.LinkDefinition && link.Definition.FightChild != 0 && link.Definition.Object.WeaponIndex != 0 ? link : null;
        }
    }

    /// <summary>
    /// Marks the sprite as referred to by another sprite.
    /// </summary>
    public void MarkReferenced() => IsReferenced = true;

    private static Sprite? MarkReferenced(Sprite? sprite)
    {
        sprite?.MarkReferenced();

        return sprite;
    }

    /// <summary>
    /// Starts the sprite in a state and draws its starting frame.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="time">The creation time.</param>
    /// <param name="random">The random source.</param>
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
        ChangeDirection(direction);

        if (state == SpriteAnimation.Stand && LastFrame - FirstFrame > 0 && !firstFrame)
        {
            Frame += random.Next(LastFrame - FirstFrame + 1);
        }
    }

    /// <summary>
    /// Changes the sprite's state, taking its links into death with it.
    /// </summary>
    /// <param name="state">The state.</param>
    [EngineRoutine(0x00442ee0)]
    public void ChangeAnimation(SpriteAnimation state)
    {
        var link = _link;

        if (link is not null && link.Definition == Definition.LinkDefinition && (state >= SpriteAnimation.Death || link.Definition.Object.WeaponIndex == 0 &&
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

    [EngineRoutine(0x00440030)]
    internal void ChangeCoor(Vector3 position)
    {
        var delta = position - Position;
        Shift(delta);

        foreach (var link in EnumerateLinks())
        {
            link.Shift(delta);
        }
    }

    private void Shift(Vector3 delta)
    {
        Position += delta;
        Collector?.ChangeCoor(this);
    }

    /// <summary>
    /// Turns the sprite, placing its link at the offset for the new direction and turning the links that keep their owner's direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    [EngineRoutine(0x00442be0)]
    public void ChangeDirection(Angle direction)
    {
        var shown = GetShownDirection(direction);

        if (_direction == shown)
            return;

        var link = _link;

        if (link is not null)
        {
            ref readonly var linked = ref link.Definition.Object;
            bool fixedOffset = linked.Properties.HasFlag(ObjectProperties.FixedOffset);
            var offset = Definition.Object.LinkOffset;

            if (link.Definition == Definition.LinkDefinition && !fixedOffset && (offset.X != 0 || offset.Y != 0))
            {
                var linkOffset = Definition.GetLinkOffset(link.Definition, Definition.GetFrameDirection(shown));
                link.ChangeCoor(new Vector3(linkOffset.AsVector2() + Position.AsVector2(), link.Position.Z));
            }
            else if (linked.SpriteClass == SpriteClass.Linker && !fixedOffset)
            {
                link.LinkRotate(Definition.GetFrameDirection(shown));
            }

            if (linked.RotationPeriod.IsNear(SpriteDefinition.Unlimited))
            {
                link.ChangeDirection(direction);
            }
        }

        var layout = Definition.Layout;

        if (layout.Directions != 1)
        {
            int frameOffset = Frame - FirstFrame;
            FirstFrame = layout.GetFirstFrame(Animation) + shown.GetDirectionIndex(layout.Directions) * layout.GetFramesPerDirection(Animation);
            LastFrame = FirstFrame + layout.GetFramesPerDirection(Animation) - 1;
            Frame = FirstFrame + frameOffset;
        }

        _direction = direction;
    }

    [EngineRoutine(0x00442e50)]
    private void LinkRotate(Angle direction)
    {
        var origin = _linkerBase ?? _owner;

        if (origin is null)
            return;

        var turn = new Angle(unchecked((byte)(direction.Value - LinkerDirection.Value)));
        var turned = new Vector2(turn.Cosine, turn.ProjectedSine) * LinkerOffset.X + new Vector2(-turn.Sine, turn.ProjectedCosine) * LinkerOffset.Y;
        ChangeCoor(new Vector3(origin.Position.AsVector2() + turned, Position.Z));
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
    /// Stops the sprite's move toward its goal.
    /// </summary>
    public void ClearMove()
    {
        IsMoving = false;
        HasReachedX = false;
        HasReachedY = false;
    }

    /// <summary>
    /// Returns the ammunition the sprite holds when full.
    /// </summary>
    /// <returns>The capacity in rounds.</returns>
    [EngineRoutine(0x0043a200)]
    public int GetAmmoCapacity()
    {
        var link = Definition.LinkDefinition;

        return link is not null && link.FightChild != 0 && link.Object.WeaponIndex != 0 ? link.AmmoCapacity : Definition.AmmoCapacity;
    }

    /// <summary>
    /// Fills the sprite's ammunition.
    /// </summary>
    public void FillAmmo() => Ammo = GetAmmoCapacity() * AmmoScale;
    
    /// <summary>
    /// Returns the rounds the sprite holds for a weapon slot.
    /// </summary>
    /// <param name="slot">The slot, or 0 for the held weapon.</param>
    /// <returns>The rounds.</returns>
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

    private int GetOwnAmmo() => Ammo / AmmoScale;

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
    internal AttackResult AttackTact(int elapsed)
    {
        var link = ArmedLink;

        if (link is not null)
        {
            var result = link.AttackTact(elapsed);

            if (result != AttackResult.NoTarget)
                return result;

            return HasGoal ? AttackResult.NoOrder : AttackResult.NoTarget;
        }

        if (Definition.FightChild == 0 || Definition.Object.WeaponIndex == 0)
            return AttackResult.Unarmed;

        if (!HasGoal)
        {
            FaceOwner(elapsed, Timer == 0);

            return AttackResult.NoTarget;
        }

        if (IsReloading)
        {
            RotateTact(DirectionTo(), elapsed);

            return AttackResult.Busy;
        }

        if (Command is not (Command.Attack or Command.OneAttack or Command.DistAttack))
        {
            FaceOwner(elapsed, Timer == 0);

            return AttackResult.NoOrder;
        }

        float distance = Position.AsVector2().MeasureOctagonalDistance(GoalPosition.AsVector2());
        bool inRange = Owner is null || Command == Command.DistAttack || distance < Definition.BattleRange && distance > Definition.MinRange;
        bool aimed = inRange && (RotateTact(DirectionTo(), elapsed) == default || Definition.AttacksWhileTurning);

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

        if (Command == Command.Attack)
            return AttackResult.Approach;

        return distance < Definition.DetectRange * 2 ? AttackResult.Pursue : AttackResult.GiveUp;
    }

    private void FaceOwner(int elapsed, bool idle)
    {
        if (Owner is not null && idle)
        {
            RotateTact(Owner.Direction, elapsed);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the sprite is between attacks.
    /// </summary>
    public bool IsReloading => Timer > ReloadTimerOffset || Animation == SpriteAnimation.Fight && Frame <= LastFrame;

    [EngineRoutine(0x004463a0)]
    private Angle DirectionTo() => Angle.Aim((GoalPosition - Position).AsVector2());

    /// <summary>
    /// Turns the sprite toward a direction, as far as its turn rate allows in the elapsed time.
    /// </summary>
    /// <param name="goal">The direction to turn to.</param>
    /// <param name="elapsed">The elapsed time in milliseconds.</param>
    /// <returns>The angle left to turn.</returns>
    [EngineRoutine(0x00444820)]
    public Angle RotateTact(Angle goal, int elapsed)
    {
        ChangeDirection(Direction.TurnToward(goal, Definition.Object.RotationPeriod, elapsed, out var remaining));

        return remaining;
    }

    /// <summary>
    /// Returns which of a mirrored pair of children the next launch takes, alternating for definitions that attack twice.
    /// </summary>
    /// <param name="animation">The state whose children launch.</param>
    /// <param name="owner">The definition whose children launch, or null for the sprite's definition.</param>
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
    /// Returns the direction the sprite turns to toward a goal direction during a frame.
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
    /// Hides the sprite and every sprite of its link chain.
    /// </summary>
    [EngineRoutine(0x00442620)]
    public void InvisibleOn()
    {
        IsInvisible = true;

        foreach (var link in EnumerateLinks())
        {
            link.IsInvisible = true;
        }
    }

    /// <summary>
    /// Shows the sprite and every sprite of its link chain.
    /// </summary>
    [EngineRoutine(0x00442640)]
    public void InvisibleOff()
    {
        IsInvisible = false;

        foreach (var link in EnumerateLinks())
        {
            link.IsInvisible = false;
        }
    }

    /// <summary>
    /// Traverses over the sprite's <see cref="Link"/> chain.
    /// </summary>
    /// <returns>The chain as an <see cref="IEnumerable{T}"/>.</returns>
    public IEnumerable<Sprite> EnumerateLinks()
    {
        for (var link = Link; link is not null; link = link.Link)
        {
            yield return link;
        }
    }

    bool IResettable.TryReset() => !IsReferenced;
}

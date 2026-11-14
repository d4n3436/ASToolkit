using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class Unit
{
    private const int ReloadTimerOffset = 5000;
    private const int AmmoScale = 64;
    private const float VelocityScale = 1e6f;
    private const int SpawnScatter = 8;
    private const float ReachTolerance = 0.5f;
    private bool _secondChildPulse;
    private Facing _facing;
    private int _createdAt;
    private Unit? _owner;
    private Unit? _link;
    private Unit? _goal;
    private Unit? _shooter;

    public Unit()
    {
        Kind = null!;
        Orders = [];
    }

    public Unit(int number, ObjectDefinition kind, Vector3 position, int army, int hp)
    {
        Initialize(number, kind, position, army, hp);
    }

    public int Number { get; private set; }

    public ObjectDefinition Kind { get; private set; }

    public int Army { get; private set; }

    public MergedMapSprite? Placement { get; set; }

    public Vector3 Position { get; set; }

    public CollisionVolume CollisionVolume => Kind.GetCollisionVolume(Position);

    public float Speed { get; set; }

    public int TurnTicks { get; set; }

    public float TurnCarry { get; set; }

    public UnitCommand Command { get; set; }

    public bool Moving { get; set; }

    public bool LeavesEffects { get; set; }

    public bool ReachedX { get; set; }

    public bool ReachedY { get; set; }

    public int Hp { get; set; }

    public KnownObject DropItem { get; set; }

    public int Bucket { get; set; }

    public int Slot { get; set; }

    public UnitOrder[] Orders { get; set; }

    public int OrderStep { get; set; }

    public int DiedAt { get; set; }

    public Facing Facing { get => _facing; init => _facing = value; }

    public SpriteAnimation Animation { get; private set; }

    public int FirstFrame { get; private set; }

    public int Frame { get; set; }

    public int LastFrame { get; private set; }

    public int LastTickTime { get; set; }

    public int CreatedAt
    {
        get => _createdAt;
        set
        {
            _createdAt = value;
            KeyframeStart = value;
            KeyframePosition = 0;
        }
    }

    public int KeyframeStart { get; set; }

    public float KeyframePosition { get; set; }

    public int Lifetime { get; set; }

    public int Timer { get; set; }

    public int Ammo { get; private set; }

    public int Rounds => Ammo / AmmoScale;

    public int HpBeforeDeath { get; set; }

    public WeaponBehavior Behavior { get; set; }

    public int StreamPeriod { get; set; }

    public bool AcquiresTargets => Behavior.HasFlag(WeaponBehavior.AcquiresTargets);

    public bool PursuesTargets => Behavior.HasFlag(WeaponBehavior.PursuesTargets);

    public float VerticalSpeed { get; set; }

    public float MaxSpeed { get; set; }

    public Unit? Goal { get => _goal; set => _goal = MarkReferenced(value); }

    public Vector3? GoalPoint { get; set; }

    public Unit? Shooter { get => _shooter; set => _shooter = MarkReferenced(value); }

    public Unit? Owner { get => _owner; set => _owner = MarkReferenced(value); }

    public Vector3 OwnerOffset { get; set; }

    public Unit? Link { get => _link; set => _link = MarkReferenced(value); }

    public int Depth { get; set; }

    public bool Blocked { get; set; }

    public bool Climbing { get; set; }

    public bool HasGoal => Goal is not null || GoalPoint is not null;

    public Vector3 GoalPosition => Goal?.Position ?? GoalPoint.GetValueOrDefault();

    public bool IsAlive => Hp > 0;

    public bool IsReferenced { get; private set; }

    // Swaps the object the unit shows and acts as, keeping its place and health (ACT_CHANGE_VID)
    public void ChangeKind(ObjectDefinition kind)
    {
        Kind = kind;
        ClearMove();
        SetState(SpriteAnimation.Stand);
    }

    [MemberNotNull(nameof(Kind), nameof(Orders))]
    public void Initialize(int number, ObjectDefinition kind, Vector3 position, int army, int hp, Facing facing = default)
    {
        _secondChildPulse = false;
        StreamPeriod = 1;
        _facing = facing;
        _owner = null;
        _link = null;
        _goal = null;
        _shooter = null;
        Number = number;
        Kind = kind;
        Behavior = kind.Behavior;
        Army = army;
        Placement = null;
        Position = position;
        Speed = 0;
        TurnTicks = 0;
        TurnCarry = 0;
        Command = default;
        Moving = false;
        ReachedX = false;
        ReachedY = false;
        Hp = hp;
        DropItem = 0;
        Bucket = -1;
        Slot = -1;
        Orders = [];
        OrderStep = 0;
        DiedAt = -1;
        Animation = default;
        FirstFrame = kind.Layout.GetFirstFrame(SpriteAnimation.Stand);
        Frame = FirstFrame;
        LastFrame = FirstFrame + kind.Layout.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
        LastTickTime = 0;
        CreatedAt = -1;
        Lifetime = kind.Object.Lifetime;
        Timer = 0;
        Ammo = GetAmmoCapacity() * AmmoScale;
        VerticalSpeed = 0;
        MaxSpeed = kind.Speed;
        GoalPoint = null;
        OwnerOffset = Vector3.Zero;
        Depth = 0;
        Blocked = false;
        Climbing = false;
        IsReferenced = false;
    }

    public void MarkReferenced() => IsReferenced = true;

    private static Unit? MarkReferenced(Unit? unit)
    {
        unit?.MarkReferenced();

        return unit;
    }

    [EngineRoutine(0x0043f200)]
    [EngineRoutine(0x0043f200)]
    public void Start(SpriteAnimation state, Facing facing, int time, Random random)
    {
        if (Kind.Object.Flags.HasFlag(ObjectFlags.OrientedBox))
        {
            float x = Position.X + SpawnScatter - random.Next() % (SpawnScatter * 2 + 1);
            float y = Position.Y + SpawnScatter - random.Next() % (SpawnScatter * 2 + 1);
            Position = Position with { X = x, Y = y };
        }

        var layout = Kind.Layout;
        bool firstFrame = Kind.Object.Flags.HasFlag(ObjectFlags.StartsOnFirstFrame);
        LastTickTime = time - (firstFrame ? 0 : random.Next(Kind.SpriteSpeed + 1));
        _facing = default;
        Animation = state;
        FirstFrame = layout.GetFirstFrame(state);
        Frame = FirstFrame;
        LastFrame = layout.GetFramesPerDirection(state) - 1;
        SetFacing(facing);

        if (state == SpriteAnimation.Stand && LastFrame - FirstFrame > 0 && !firstFrame)
        {
            Frame += random.Next(LastFrame - FirstFrame + 1);
        }
    }

    [EngineRoutine(0x00442ee0)]
    public void SetState(SpriteAnimation state)
    {
        var link = _link;

        if (link is not null && link.Kind.Id == Kind.Object.Link && (state >= SpriteAnimation.Death || link.Kind.Object.WeaponIndex == 0 &&
            !link.Kind.Object.Flags.HasFlag(ObjectFlags.Windblown) && link.Animation is not SpriteAnimation.Fight and < SpriteAnimation.Death))
        {
            link.SetState(state);
        }

        if (Animation == state)
            return;

        var layout = Kind.Layout;
        var facing = GetShownFacing(_facing);

        FirstFrame = layout.GetFirstFrame(state) + layout.GetDirectionIndex(facing) * layout.GetFramesPerDirection(state);
        Frame = FirstFrame;
        Animation = state;
        LastFrame = state > SpriteAnimation.Unload && layout.GetFrameCount(state) == 0
            ? FirstFrame : FirstFrame + layout.GetFramesPerDirection(state) - 1;
    }

    [EngineRoutine(0x00442be0)]
    public void SetFacing(Facing facing)
    {
        var shown = GetShownFacing(facing);

        if (_facing == shown)
            return;

        var link = _link;

        if (link is not null && link.Kind.Id == Kind.Object.Link && link.Kind.Object.TurnPeriod.IsZero())
        {
            link.SetFacing(facing);
        }

        var layout = Kind.Layout;

        if (layout.Directions != 1)
        {
            int offset = Frame - FirstFrame;
            FirstFrame = layout.GetFirstFrame(Animation) + layout.GetDirectionIndex(shown) * layout.GetFramesPerDirection(Animation);
            LastFrame = FirstFrame + layout.GetFramesPerDirection(Animation) - 1;
            Frame = FirstFrame + offset;
        }

        _facing = facing;
    }

    // Pick the frames of a falling motion-tracking object by its screen velocity
    private Facing GetShownFacing(Facing facing)
    {
        if (VerticalSpeed.IsZero() || !Kind.Object.Flags.HasFlag(ObjectFlags.TracksMotion))
            return facing;

        var direction = facing.Direction;

        return Facing.FromOffset(new Vector2(direction.X * Speed * VelocityScale, -(direction.Y * Speed + VerticalSpeed) * VelocityScale));
    }

    public void UpdateReach(Vector3 from, Vector3 next, Vector2 goal)
    {
        ReachedX |= Math.Min(from.X, next.X) - ReachTolerance <= goal.X && goal.X <= Math.Max(from.X, next.X) + ReachTolerance;
        ReachedY |= Math.Min(from.Y, next.Y) - ReachTolerance <= goal.Y && goal.Y <= Math.Max(from.Y, next.Y) + ReachTolerance;
    }

    public void ClearMove()
    {
        Moving = false;
        ReachedX = false;
        ReachedY = false;
    }

    [EngineRoutine(0x004443f0)]
    public void StartMoving(float maxSpeed)
    {
        Moving = true;
        ReachedX = false;
        ReachedY = false;

        if (Animation < SpriteAnimation.Death && Animation != SpriteAnimation.StartMove)
        {
            SetState(Speed.IsZero() ? SpriteAnimation.StartMove : SpriteAnimation.Go);
        }

        if (Kind.Object.Acceleration.IsNear(ObjectDefinition.Unlimited))
        {
            Speed = maxSpeed;
        }
    }

    public Unit? ArmedLink
    {
        get
        {
            var link = Link;
            return link is not null && link.Kind.Id == Kind.Object.Link && link.Kind.Object.WeaponIndex != 0 ? link : null;
        }
    }

    [EngineRoutine(0x0043a200)]
    private int GetAmmoCapacity() => (ArmedLink?.Kind ?? Kind).AmmoCapacity;

    [EngineRoutine(0x00447500)]
    public void AddAmmo(int amount)
    {
        if (GetAmmoCapacity() == ObjectDefinition.Unlimited)
        {
            Ammo = ObjectDefinition.Unlimited * AmmoScale;

            return;
        }

        Ammo = Math.Max(0, Ammo + amount * AmmoScale);
    }

    [EngineRoutine(0x004460b0)]
    public AttackResult TryAttack(int elapsed, PlayerInventory? ammo = null, int slot = 0)
    {
        var link = ArmedLink;

        if (link is not null)
        {
            var result = link.TryAttack(elapsed);

            if (result != AttackResult.NoTarget)
                return result;

            return HasGoal ? AttackResult.NoOrder : AttackResult.NoTarget;
        }

        if (!Kind.IsArmed || Kind.FightChild == 0)
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

        float distance = Position.AsVector2().MeasureDistance(GoalPosition.AsVector2());
        bool inRange = Owner is null || Command == UnitCommand.DistAttack || distance < Kind.BattleRange && distance > Kind.MinRange;
        bool aimed = inRange && (Aim(elapsed) == 0 || Kind.AttacksWhileTurning);

        if (!inRange)
        {
            FaceOwner(elapsed, true);
        }

        if (distance <= Kind.BattleRange)
        {
            if (!aimed)
                return AttackResult.Busy;

            if (ammo is not null && !ammo.TrySpendAmmo(slot))
                return AttackResult.NoAmmo;

            int cost = Math.Abs(Kind.Object.ChildCounts[(int)SpriteAnimation.Fight]);

            if (ammo is null)
            {
                if (Rounds < cost)
                    return AttackResult.NoAmmo;

                AddAmmo(-cost);
            }

            SetState(SpriteAnimation.Fight);
            Timer = Kind.ReloadTime + ReloadTimerOffset;

            return AttackResult.Attacked;
        }

        if (Command == UnitCommand.Attack)
            return AttackResult.Approach;

        return distance < Kind.DetectRange * 2 ? AttackResult.Pursue : AttackResult.GiveUp;
    }

    private void FaceOwner(int elapsed, bool idle)
    {
        if (Owner is not null && idle)
        {
            SetFacing(Facing.TurnToward(Owner.Facing, Kind.Object.TurnPeriod, elapsed, out _));
        }
    }

    public bool IsReloading => Timer > ReloadTimerOffset || Animation == SpriteAnimation.Fight && Frame <= LastFrame;

    [EngineRoutine(0x004463a0)]
    private int Aim(int elapsed)
    {
        var facing = Facing.Aim((GoalPosition - Position).AsVector2());
        SetFacing(Facing.TurnToward(facing, Kind.Object.TurnPeriod, elapsed, out int remaining));

        return remaining;
    }

    public bool TakeChildPulse(SpriteAnimation animation, ObjectDefinition? owner = null)
    {
        owner ??= Kind;
        bool second = _secondChildPulse;

        if (owner.AttacksTwice && owner.Object.Children[(int)animation] != 0 && Math.Abs(owner.Object.ChildCounts[(int)animation]) == ChildEmission.MirroredPairCount)
        {
            _secondChildPulse = !second;
        }

        return second;
    }

    public UnitState GetState() =>
        new(Number, Kind.Id, Position, Facing.Value, Animation, DiedAt, Frame, Keyframe: KeyframePosition);

    public Facing TurnWhileMoving(Facing goal, int elapsed)
    {
        float period = Kind.Object.TurnPeriod;

        if (elapsed >= GameClock.DefaultFrameTime || Facing == goal || period.IsZero() || period.IsNear(ObjectDefinition.Unlimited))
        {
            TurnCarry = 0;

            return Facing.TurnToward(goal, period, elapsed, out _);
        }

        // Carry the unrounded turn to the next frame above 60 frames a second
        float turn = elapsed * (Facing.FullTurn / period) + TurnCarry;
        int step = (int)turn;
        TurnCarry = turn - step;

        return Facing.StepToward(goal, step, out _);
    }
}

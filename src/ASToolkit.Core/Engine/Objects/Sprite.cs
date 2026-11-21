using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents an object on the running map, such as a monster, a pickup, a door or an effect.
/// </summary>
public sealed class Sprite
{
    /// <summary>
    /// Represents the object ID of the torso for weapon slot 0.
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

    internal const int RegionPairs = 6;
    private const int ReloadTimerOffset = 5000;
    private const int AmmoScale = 64;
    private const int MaxWeaponSlot = 9;
    private const int ArmyMask = 3;
    private const int MaxAnimation = 16;
    private const int HpShare = 0xff;
    private const float VelocityScale = 1e6f;
    private const float ReachTolerance = 0.5f;

    private int _lifetime;
    private float _maxSpeed;
    private Angle _direction;

    /// <summary>
    /// Initializes a new instance of the <see cref="Sprite"/> class from a definition.
    /// </summary>
    /// <param name="number">The sprite number.</param>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="army">The army.</param>
    /// <param name="hitPoints">The health.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="map">The map the sprite belongs to, which supplies the game time and the random scatter of the position.</param>
    /// <param name="parent">The sprite that creates this sprite and whose height a ground-bound object is placed relative to.</param>
    public Sprite(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Angle direction = default, Map? map = null, Sprite? parent = null)
    {
        Map = map;
        StreamPeriod = 1;
        _direction = direction;
        Number = number;
        Definition = definition;
        Behavior = definition.Behavior;
        Army = army;
        Position = map?.ScatterPosition(definition, position, parent) ?? position;
        Hp = hitPoints;
        Bucket = -1;
        Slot = -1;
        ListIndex = -1;
        DiedAt = -1;
        FirstFrame = definition.Layout.GetFirstFrame(SpriteAnimation.Stand);
        Frame = FirstFrame;
        LastFrame = FirstFrame + definition.Layout.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
        CreatedAt = -1;
        FillAmmo();

        if (definition.HasTimers)
        {
            CreateExtra();
        }
    }

    /// <summary>
    /// Gets the sprite number.
    /// </summary>
    public int Number { get; private set; }

    /// <summary>
    /// Gets or sets the handle the level script refers to the sprite by, or 0 if the sprite doesn't have one.
    /// </summary>
    public int Handle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite counts toward its definition's per-army count.
    /// </summary>
    public bool IsCounted { get; set; }

    /// <summary>
    /// Gets the definition of the sprite.
    /// </summary>
    public SpriteDefinition Definition { get; private set; }

    /// <summary>
    /// Gets or sets the army of the sprite.
    /// </summary>
    public Army Army { get; set; }

    /// <summary>
    /// Gets or sets the map sprite this sprite was built from.
    /// </summary>
    public MergedMapSprite? Placement { get; set; }

    /// <summary>
    /// Gets or sets the position of the sprite.
    /// </summary>
    public Vector3 Position { get; set; }

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
    /// Gets or sets the command the sprite carries out.
    /// </summary>
    public Command Command { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite moves toward its goal.
    /// </summary>
    public bool IsMoving { get; set; }

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
    public int CreatedAt { get; set; }

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
    public int Lifetime
    {
        get => HasExtra ? _lifetime : SpriteDefinition.Unlimited;
        set
        {
            if (!HasExtra)
            {
                CreateExtra();
            }

            _lifetime = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the sprite carries a lifetime, a keyframe track, a stream period and a top speed of its own.
    /// </summary>
    public bool HasExtra { get; private set; }

    /// <summary>
    /// Gets the position the sprite had before its first move of the frame.
    /// </summary>
    public Vector3 PreviousPosition { get; private set; }

    /// <summary>
    /// Gets the clock stamp of the frame that saved <see cref="PreviousPosition"/>.
    /// </summary>
    public uint PreviousPositionStamp { get; private set; }

    /// <summary>
    /// Gets the gamma that tints the sprite while it is drawn, or the default gamma for no tint.
    /// </summary>
    public Gamma Tint { get; private set; }

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
    public bool ShouldRefillAmmo { get; set; }

    /// <summary>
    /// Gets the weapon slot of the torso the sprite's definition links.
    /// </summary>
    public int WeaponSlot => (int)(Definition.LinkDefinition?.Id ?? 0) - WeaponIdBase;

    /// <summary>
    /// Gets the ammunition the sprite keeps for the slots it does not hold.
    /// </summary>
    public int[] SlotAmmo => field ??= new int[MaxWeaponSlot + 1];

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
    public float MaxSpeed
    {
        get => HasExtra ? _maxSpeed : Definition.Speed;
        set
        {
            if (!HasExtra)
            {
                CreateExtra();
            }

            _maxSpeed = value;
        }
    }

    /// <summary>
    /// Gets or sets the sprite this sprite moves toward or attacks, holding a reference to it.
    /// </summary>
    public Sprite? Goal
    {
        get;
        [EngineRoutine(0x00444650)]
        set
        {
            if (field == value)
                return;

            field?.Release();
            field = value;
            field?.AddRef();
        }
    }

    /// <summary>
    /// Gets or sets the sprite that fired this sprite.
    /// </summary>
    public Sprite? Shooter { get; set; }

    /// <summary>
    /// Gets or sets the sprite this sprite is linked to.
    /// </summary>
    public Sprite? Owner { get; set; }

    /// <summary>
    /// Gets or sets the sprite a linker turns around, the parent it was created with.
    /// </summary>
    public Sprite? LinkerBase { get; set; }

    /// <summary>
    /// Gets or sets a linker's offset from its base when it was created.
    /// </summary>
    public Vector3 LinkerOffset { get; set; }

    /// <summary>
    /// Gets or sets the direction a linker faced when it was created.
    /// </summary>
    public Angle LinkerDirection { get; set; }

    /// <summary>
    /// Gets or sets the next sprite of this sprite's link chain.
    /// </summary>
    public Sprite? Link { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite's last move was blocked.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite climbs onto higher ground.
    /// </summary>
    public bool IsClimbing { get; set; }

    /// <summary>
    /// Gets a value indicating whether the sprite has a goal.
    /// </summary>
    public bool HasGoal => Goal is not null;

    /// <summary>
    /// Gets the position of the sprite's goal.
    /// </summary>
    public Vector3 GoalPosition => Goal?.Position ?? default;

    /// <summary>
    /// Gets a value indicating whether the sprite has health left and hasn't entered a death state.
    /// </summary>
    public bool IsAlive => Hp > 0 && Animation < SpriteAnimation.Death;

    /// <summary>
    /// Gets the number of lists and sprites that hold this sprite.
    /// </summary>
    public int ReferenceCount { get; private set; }

    /// <summary>
    /// Gets this sprite's link when it is the armed object its definition links, or null.
    /// </summary>
    public Sprite? ArmedLink
    {
        get
        {
            var link = Link;
            return link is not null && link.Definition == Definition.LinkDefinition && link.Definition.HasFightWeapon() ? link : null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the sprite is between attacks.
    /// </summary>
    public bool IsReloading => Timer > ReloadTimerOffset || Animation == SpriteAnimation.Fight && Frame <= LastFrame;

    /// <summary>
    /// Gets or sets a value indicating whether the sound of the current state was requested.
    /// </summary>
    public bool HasPlayedStateSound { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first of a mirrored pair of children launched and the second is still due.
    /// </summary>
    public bool IsAwaitingSecondPulse { get; set; }

    internal GameLogger? Logger { get; set; }

    internal Map? Map { get; set; }

    internal AttackResult AttackResult { get; set; }

    internal bool SeeksRegion { get; set; }

    internal Sprite? Region { get; set; }

    internal bool IsCoveringMap { get; set; }

    internal SpriteDefinition?[] RegionFrom => field ??= new SpriteDefinition?[RegionPairs];

    internal SpriteDefinition?[] RegionTo => field ??= new SpriteDefinition?[RegionPairs];

    internal SpriteCollector? Collector { get; set; }

    internal SpriteText? Text { get; set; }

    /// <summary>
    /// Adds an item at the end of the sprite's items.
    /// </summary>
    /// <param name="item">The item.</param>
    [EngineRoutine(0x00446760)]
    public void InsertItem(int item)
    {
        if (!HasExtra)
        {
            CreateExtra();
        }

        Items.Add(item);
    }

    /// <summary>
    /// Adds an item unless the sprite already holds it.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if the item was added; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00446810)]
    public bool InsertUniqueItem(int item)
    {
        if (GetItemNumber(item) >= 0)
            return false;

        InsertItem(item);

        return true;
    }

    /// <summary>
    /// Returns the index of an item, searching from the end.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The index, or -1 when the sprite does not hold the item.</returns>
    [EngineRoutine(0x00446900)]
    public int GetItemNumber(int item) => Items.LastIndexOf(item);

    /// <summary>
    /// Removes the item at an index by moving the last item into its place.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns><see langword="true"/> if the index named an item; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00446bb0)]
    public bool RemoveItemAt(int index)
    {
        if (index < 0 || index >= Items.Count)
            return false;

        Items[index] = Items[^1];
        Items.RemoveAt(Items.Count - 1);

        return true;
    }

    /// <summary>
    /// Returns the health of this sprite, or of its armed link, scaled from 0 to 255 of the maximum.
    /// </summary>
    /// <returns>The share.</returns>
    [EngineRoutine(0x00444360)]
    public int GetPercentHp()
    {
        var link = ArmedLink;

        if (link is not null)
        {
            int linkMax = link.Definition.ArmyMaxHp[(int)link.Army & ArmyMask];

            if (linkMax != 0)
                return link.Hp * HpShare / linkMax;
        }

        int max = Definition.ArmyMaxHp[(int)Army & ArmyMask];

        return max != 0 ? Hp * HpShare / max : 0;
    }

    /// <summary>
    /// Determines whether this sprite or its armed link can attack the type of another sprite.
    /// </summary>
    /// <param name="target">The sprite to attack.</param>
    /// <returns><see langword="true"/> if a weapon of this sprite targets the type of <paramref name="target"/>; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x00445f30)]
    public bool CanAttackThisSprite(Sprite? target)
    {
        if (target is null)
            return false;

        if (Definition.FightChild != 0 && Definition.Object.WeaponIndex != 0 && (Definition.Targets & target.Definition.Object.SpriteType) != 0)
            return true;

        var link = ArmedLink;

        return link is not null && (link.Definition.Targets & target.Definition.Object.SpriteType) != 0;
    }

    /// <summary>
    /// Returns the direction from this sprite to another sprite.
    /// </summary>
    /// <param name="other">The other sprite.</param>
    /// <returns>The direction.</returns>
    [EngineRoutine(0x004474b0)]
    public Angle MeasureDirectionTo(Sprite other) => Angle.FromOffset((other.Position - Position).AsVector2());

    /// <summary>
    /// Creates the sprite's extra data if it does not exist yet.
    /// </summary>
    public void EnsureExtra()
    {
        if (!HasExtra)
        {
            CreateExtra();
        }
    }

    /// <summary>
    /// Gives the sprite its own lifetime, keyframe track, stream period and top speed.
    /// </summary>
    [EngineRoutine(0x0043f1a0)]
    public void CreateExtra()
    {
        HasExtra = true;
        StreamPeriod = 1;
        _lifetime = Definition.Lifetime;
        KeyframePosition = 0;
        KeyframeStart = Map?.Time ?? 0;
        _maxSpeed = Definition.Speed;
        PreviousPosition = Position;
    }

    /// <summary>
    /// Sets the gamma that tints the sprite while it is drawn, creating the sprite's extra data if needed.
    /// </summary>
    /// <param name="gamma">The gamma.</param>
    [EngineRoutine(0x00443070)]
    public void SetGammaOverride(Gamma gamma)
    {
        if (!HasExtra)
        {
            CreateExtra();
        }

        Tint = gamma;
    }

    /// <summary>
    /// Changes the sprite's definition, moves its count and resets the sprite to the first frame of the standing state, facing up.
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

        if (definition.HasTimers)
        {
            if (HasExtra)
            {
                _lifetime = definition.Lifetime;
            }
            else
            {
                CreateExtra();
            }
        }

        Animation = SpriteAnimation.Stand;
        _direction = default;
        FirstFrame = 0;
        Frame = 0;
        LastFrame = definition.Layout.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
    }

    /// <summary>
    /// Adds a reference to the sprite.
    /// </summary>
    public void AddRef() => ReferenceCount++;

    /// <summary>
    /// Removes a reference to the sprite.
    /// </summary>
    /// <returns>The number of references left.</returns>
    public int Release() => --ReferenceCount;

    /// <summary>
    /// Starts the sprite in a state and draws its starting frame.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="time">The creation time.</param>
    /// <param name="random">The random source.</param>
    public void Start(SpriteAnimation state, Angle direction, int time, Random random)
    {
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
    /// Changes the sprite's state and takes its links into death with it.
    /// </summary>
    /// <param name="state">The state.</param>
    [EngineRoutine(0x00442ee0)]
    public void ChangeAnimation(SpriteAnimation state)
    {
        if ((int)state > MaxAnimation)
        {
            Logger?.Error("SPRITE %i"u8, LogError.Invalid, 0, default);

            return;
        }

        var link = Link;

        if (link is not null && link.Definition == Definition.LinkDefinition && (state >= SpriteAnimation.Death || link.Definition.Object.WeaponIndex == 0 &&
            !link.Definition.Object.Properties.HasFlag(ObjectProperties.Wind) && link.Animation is not SpriteAnimation.Fight and < SpriteAnimation.Death))
        {
            link.ChangeAnimation(state);
        }

        if (Animation == state)
            return;

        HasPlayedStateSound = false;
        var layout = Definition.Layout;
        var direction = GetShownDirection(_direction);

        FirstFrame = layout.GetFirstFrame(state) + layout.GetDirectionIndex(direction) * layout.GetFramesPerDirection(state);
        Frame = FirstFrame;
        Animation = state;
        LastFrame = state > SpriteAnimation.Unload && layout.GetFrameCount(state) == 0
            ? FirstFrame : FirstFrame + layout.GetFramesPerDirection(state) - 1;
    }

    /// <summary>
    /// Turns the sprite, places its link at the offset for the new direction and turns the links that keep their owner's direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    [EngineRoutine(0x00442be0)]
    public void ChangeDirection(Angle direction)
    {
        var shown = GetShownDirection(direction);

        if (_direction == shown)
            return;

        var link = Link;

        if (link is not null)
        {
            ref readonly var linked = ref link.Definition.Object;
            bool fixedOffset = linked.Properties.HasFlag(ObjectProperties.FixedOffset);
            var offset = Definition.Object.LinkOffset;

            if (link.Definition == Definition.LinkDefinition && !fixedOffset && (!offset.X.IsExactlyZero() || !offset.Y.IsExactlyZero()))
            {
                var linkOffset = Definition.GetLinkOffset(link.Definition, Definition.GetFrameDirection(shown));
                link.ChangeCoor(new Vector3(linkOffset.AsVector2() + Position.AsVector2(), link.Position.Z));
            }
            else if (linked.SpriteClass == SpriteClass.Linker && !fixedOffset)
            {
                link.LinkRotate(Definition.GetFrameDirection(shown));
            }

            if (linked.RotationPeriod.IsExactly(SpriteDefinition.Unlimited))
            {
                link.ChangeDirection(direction);
            }
        }

        var layout = Definition.Layout;

        if (layout.Directions != 1)
        {
            int frameOffset = Frame - FirstFrame;
            FirstFrame = layout.GetFirstFrame(Animation) + layout.GetDirectionIndex(shown) * layout.GetFramesPerDirection(Animation);
            LastFrame = FirstFrame + layout.GetFramesPerDirection(Animation) - 1;
            Frame = FirstFrame + frameOffset;
        }

        _direction = direction;
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

        return link is not null && link.HasFightWeapon() ? link.AmmoCapacity : Definition.AmmoCapacity;
    }

    /// <summary>
    /// Fills the sprite's ammunition.
    /// </summary>
    public void FillAmmo() => Ammo = GetAmmoCapacity() * AmmoScale;

    /// <summary>
    /// Returns the rounds the sprite holds for its held weapon.
    /// </summary>
    /// <returns>The rounds.</returns>
    public int GetAmmo() => GetOwnAmmo();

    /// <summary>
    /// Adds rounds to the sprite's held weapon.
    /// </summary>
    /// <param name="amount">The rounds to add, negative to spend.</param>
    /// <returns>The rounds held after the addition.</returns>
    public int AddAmmo(int amount) => AddOwnAmmo(amount);

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
    /// Returns the direction the sprite reaches when it turns toward a goal direction for one frame.
    /// </summary>
    /// <param name="goal">The goal direction.</param>
    /// <param name="elapsed">The frame's length in milliseconds.</param>
    /// <returns>The new direction.</returns>
    public Angle TurnWhileMoving(Angle goal, int elapsed)
    {
        return Direction.TurnToward(goal, Definition.Object.RotationPeriod, elapsed, out _);
    }

    /// <summary>
    /// Hides this sprite and every sprite of its link chain.
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
    /// Shows this sprite and every sprite of its link chain.
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
    /// Returns the sprites of this sprite's <see cref="Link"/> chain, nearest first.
    /// </summary>
    /// <returns>The sprites of the chain.</returns>
    public IEnumerable<Sprite> EnumerateLinks()
    {
        for (var link = Link; link is not null; link = link.Link)
        {
            yield return link;
        }
    }

    internal void ReplaceDefinition(SpriteDefinition definition) => Definition = definition;

    internal void SetAnimationValue(SpriteAnimation state) => Animation = state;

    [EngineRoutine(0x00440030)]
    internal void ChangeCoor(Vector3 position)
    {
        var delta = position - Position;
        Map?.LowerHeights(Definition, this);
        Shift(delta);
        Map?.RaiseHeights(Definition, this);

        foreach (var link in EnumerateLinks())
        {
            Map?.LowerHeights(link.Definition, this);
            link.Shift(delta);
            Map?.RaiseHeights(link.Definition, this);
        }
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
            RotateTact(MeasureDirectionToGoal(), elapsed);

            return AttackResult.Busy;
        }

        if (Command is not (Command.Attack or Command.OneAttack or Command.DistAttack))
        {
            FaceOwner(elapsed, Timer == 0);

            return AttackResult.NoOrder;
        }

        float distance = Position.AsVector2().MeasureOctagonalDistance(GoalPosition.AsVector2());
        bool inRange = Owner is null || Command == Command.DistAttack || distance < Definition.BattleRange && distance > Definition.MinRange;
        bool aimed = inRange && (RotateTact(MeasureDirectionToGoal(), elapsed) == default || Definition.CanAttackWhileTurning);

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

    private void Shift(Vector3 delta)
    {
        uint stamp = Map?.WallTime ?? 0;

        if (HasExtra && PreviousPositionStamp != stamp)
        {
            PreviousPositionStamp = stamp;
            PreviousPosition = Position;
        }

        Position += delta;
        Collector?.ChangeCoor(this);
    }

    [EngineRoutine(0x00442e50)]
    private void LinkRotate(Angle direction)
    {
        var origin = LinkerBase ?? Owner;

        if (origin is null)
            return;

        var turn = new Angle(unchecked((byte)(direction.Value - LinkerDirection.Value)));
        var turned = new Vector2(turn.Cosine, turn.ProjectedSine) * LinkerOffset.X + new Vector2(-turn.Sine, turn.ProjectedCosine) * LinkerOffset.Y;
        ChangeCoor(new Vector3(origin.Position.AsVector2() + turned, Position.Z));
    }

    private Angle GetShownDirection(Angle direction)
    {
        if (VerticalSpeed.IsExactlyZero() || !Definition.Object.Properties.HasFlag(ObjectProperties.VerticalDirection))
            return direction;

        return Angle.FromOffset(new Vector2(direction.Vector.X * Speed * VelocityScale, -(direction.Vector.Y * Speed + VerticalSpeed) * VelocityScale));
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

        if (ShouldRefillAmmo)
        {
            Ammo = GetAmmoCapacity() * AmmoScale;
        }

        return ammo;
    }

    private void FaceOwner(int elapsed, bool idle)
    {
        if (Owner is not null && idle)
        {
            RotateTact(Owner.Direction, elapsed);
        }
    }

    [EngineRoutine(0x004463a0)]
    private Angle MeasureDirectionToGoal() => Angle.Aim((GoalPosition - Position).AsVector2());
}

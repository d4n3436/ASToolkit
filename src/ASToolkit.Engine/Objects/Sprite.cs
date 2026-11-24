using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents an object on the running map, such as a monster, a pickup, a door or an effect.
/// </summary>
public partial class Sprite
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
    private const int ReloadTimerOffset = 5000;
    private const int GammaAlphaShift = 24;
    private const int MaxAnimation = 16;
    private const int HpShare = 0xff;
    private const float VelocityScale = 1e6f;
    private const float ReachTolerance = 0.5f;
    private const float SpeedScale = 1000;
    private const float InfoTextLeft = 22;
    private const float InfoLineHeight = 12;
    private const float OrdersTextLeft = 30;

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
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction.</param>
    public Sprite(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
    {
        Map = map;
        ActionStack = new ActionStack(Map.Session.Logger);
        StreamPeriod = 1;
        _direction = direction;
        Number = number;
        Definition = definition;
        Army = army;
        Position = position;
        Hp = hitPoints;
        FirstFrame = definition.GetFirstFrame(SpriteAnimation.Stand);
        Frame = FirstFrame;
        LastFrame = FirstFrame + definition.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
        CreatedAt = -1;

        if (definition.HasTimers)
        {
            CreateExtra();
        }
    }

    /// <summary>
    /// Gets the sprite number.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Gets or sets the script handle of the sprite, or 0 if the sprite doesn't have one.
    /// </summary>
    public int Handle { get; set; }

    /// <summary>
    /// Gets the definition of the sprite.
    /// </summary>
    public SpriteDefinition Definition { get; private set; }

    /// <summary>
    /// Gets or sets the army of the sprite.
    /// </summary>
    public Army Army { get; set; }

    /// <summary>
    /// Gets or sets the position of the sprite.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the speed of the sprite in pixels per millisecond.
    /// </summary>
    public float Speed { get; set; }

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
    /// Gets the order stack of the sprite.
    /// </summary>
    public ActionStack ActionStack { get; }

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
    /// Gets a value indicating whether the sprite carries a lifetime, a keyframe track, a stream period and a top speed of its own.
    /// </summary>
    public bool HasExtra { get; private set; }

    /// <summary>
    /// Gets the position the sprite had before its first move of the frame.
    /// </summary>
    public Vector3 PreviousPosition { get; private set; }

    /// <summary>
    /// Gets the clock stamp of the frame when <see cref="PreviousPosition"/> was saved.
    /// </summary>
    public uint PreviousPositionStamp { get; private set; }

    /// <summary>
    /// Gets the gamma applied to the sprite while it is drawn, or the default gamma when there isn't one.
    /// </summary>
    public Gamma GammaOverride { get; private set; }

    /// <summary>
    /// Gets or sets the sprite's countdown timer in milliseconds.
    /// </summary>
    public int Timer { get; set; }

    /// <summary>
    /// Gets the weapon slot of the torso linked by the sprite's definition.
    /// </summary>
    public int WeaponSlot => (int)(Definition.LinkDefinition?.Id ?? 0) - WeaponIdBase;

    /// <summary>
    /// Gets or sets the period of the sprite's stream children.
    /// </summary>
    public int StreamPeriod { get; set; }

    /// <summary>
    /// Gets or sets the vertical speed of the sprite.
    /// </summary>
    public float VerticalSpeed { get; set; }

    /// <summary>
    /// Gets or sets the sprite that fired this sprite.
    /// </summary>
    public Sprite? Shooter { get; set; }

    /// <summary>
    /// Gets or sets the sprite this sprite is linked to.
    /// </summary>
    public Sprite? Owner { get; set; }

    /// <summary>
    /// Gets or sets the next sprite of this sprite's link chain.
    /// </summary>
    public Sprite? Link { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite's last move was blocked.
    /// </summary>
    public bool IsBlocked { get; set; }

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

    /// <summary>
    /// Gets or sets the time the sprite has left before it dies.
    /// </summary>
    public int Lifetime
    {
        get => HasExtra ? _lifetime : SpriteDefinition.Unlimited;
        set
        {
            EnsureExtra();

            _lifetime = value;
        }
    }

    /// <summary>
    /// Gets or sets the top speed of the sprite in pixels per millisecond.
    /// </summary>
    public float MaxSpeed
    {
        get => HasExtra ? _maxSpeed : Definition.Speed;
        set
        {
            EnsureExtra();

            _maxSpeed = value;
        }
    }

    /// <summary>
    /// Gets or sets the sprite this sprite moves toward or attacks, and holds a reference to it.
    /// </summary>
    public Sprite? Goal
    {
        get;
        [EngineRoutine(0x00444650)]
        set
        {
            if (field == value)
                return;

            var previous = field;

            if (previous is not null)
            {
                int count = previous.Release();

                if (count < 1)
                {
                    if (count < 0)
                    {
                        Logger?.Error($"SPRITE {(int)previous.Definition.Id}", LogError.Invalid, count, "noRef at Release"u8);
                    }
                    else
                    {
                        Map.DeleteSprite(previous);
                    }
                }
            }

            field = value;
            field?.AddRef();
        }
    }

    /// <summary>
    /// Gets this sprite's link if it's the armed object linked by the sprite's definition; otherwise, <see langword="null"/>.
    /// </summary>
    public Sprite? ArmedLink
    {
        get
        {
            var link = Link;
            return link is not null && link.Definition == Definition.LinkDefinition && link.Definition.HasFightWeapon() ? link : null;
        }
    }

    internal GameLogger? Logger { get; set; }

    internal Map Map { get; }

    internal AttackResult AttackResult { get; set; }

    /// <summary>
    /// Adds an item at the end of the sprite's items.
    /// </summary>
    /// <param name="item">The item.</param>
    [EngineRoutine(0x00446760)]
    public void InsertItem(int item)
    {
        EnsureExtra();

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
    /// Returns the index of an item, found by searching from the end.
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
    /// <returns>The scaled health.</returns>
    [EngineRoutine(0x00444360)]
    public int GetPercentHp()
    {
        var link = ArmedLink;

        if (link is not null)
        {
            int linkMax = link.Definition.ArmyMaxHp[(int)link.Army & SpriteDefinition.ArmyMask];

            if (linkMax != 0)
                return link.Hp * HpShare / linkMax;
        }

        int max = Definition.ArmyMaxHp[(int)Army & SpriteDefinition.ArmyMask];

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
    public Angle DirectionTo(Sprite other) => Angle.FromOffset((other.Position - Position).AsVector2());

    /// <summary>
    /// Determines whether this sprite touches a target on the ground plane.
    /// </summary>
    /// <param name="target">The center of the target.</param>
    /// <param name="targetHalf">Half the width and depth of the target.</param>
    /// <returns><see langword="true"/> if this sprite touches the target; otherwise, <see langword="false"/>.</returns>
    public bool IsTouching(Vector2 target, Vector2 targetHalf)
    {
        var reach = Definition.Half + targetHalf;

        return Math.Abs(Position.X - target.X) < reach.X && Math.Abs(Position.Y - target.Y) < reach.Y;
    }

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
        KeyframeStart = Map.Time;
        _maxSpeed = Definition.Speed;
        PreviousPosition = Position;
    }

    /// <summary>
    /// Sets the gamma applied to the sprite while it is drawn.
    /// </summary>
    /// <param name="gamma">The gamma.</param>
    [EngineRoutine(0x00443070)]
    public void SetGammaOverride(Gamma gamma)
    {
        EnsureExtra();

        GammaOverride = gamma;
    }

    /// <summary>
    /// Changes the sprite's definition, moving the sprite from the old definition's count to the new one's, and resets the sprite to the first frame of the standing state with its direction up.
    /// </summary>
    /// <param name="definition">The new definition.</param>
    public void ChangeVid(SpriteDefinition definition)
    {
        int army = (int)Army & SpriteDefinition.ArmyMask;
        if (Definition.Counts[army] != 0)
        {
            Definition.Counts[army]--;
        }

        definition.LastCreatedAt = Map.WallTime;
        definition.Counts[army]++;

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
        LastFrame = definition.GetFramesPerDirection(SpriteAnimation.Stand) - 1;
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
    /// Starts the sprite in a state and picks its starting frame.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="time">The creation time.</param>
    /// <param name="random">The random number generator.</param>
    public void Start(SpriteAnimation state, Angle direction, int time, Random random)
    {
        var layout = Definition;
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
    /// Changes the sprite's state and passes it on to its link, always for a death state and otherwise when the link is unarmed, isn't moved by
    /// the wind and isn't attacking or dying.
    /// </summary>
    /// <param name="state">The state.</param>
    [EngineRoutine(0x00442ee0)]
    public void ChangeAnimation(SpriteAnimation state)
    {
        if ((int)state > MaxAnimation)
        {
            Logger?.Error($"SPRITE {(int)Definition.Id}", LogError.Invalid, (int)state, "new_animation in ChangeAnimation"u8);

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
        var layout = Definition;
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
                ((Linker)link).LinkRotate(Definition.GetFrameDirection(shown));
            }

            if (link.Definition.RotationRate.IsExactlyZero())
            {
                link.ChangeDirection(direction);
            }
        }

        var layout = Definition;

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
    /// Turns the sprite toward a direction, as far as its turn rate allows in the elapsed time.
    /// </summary>
    /// <param name="goal">The direction to turn to.</param>
    /// <param name="elapsed">The elapsed time in milliseconds.</param>
    /// <returns>The angle left to turn.</returns>
    [EngineRoutine(0x00444820)]
    public Angle RotateTact(Angle goal, int elapsed)
    {
        var current = _direction;
        if (goal == current)
            return default;

        float rate = Definition.RotationRate;
        if (rate.IsExactly(SpriteDefinition.Unlimited))
        {
            ChangeDirection(goal);

            return 1;
        }

        int step = (int)(elapsed * rate + 0.5f);
        if (step == 0)
            return (byte)current.MeasureTurnTo(goal);

        int difference = Math.Abs(current.Value - goal.Value);
        int other = Angle.FullTurn - difference;
        bool back = false;
        bool forward = false;
        if (goal.Value < current.Value)
        {
            if (difference < other)
            {
                back = true;
            }
            else
            {
                forward = true;
            }
        }
        else if (current.Value < goal.Value)
        {
            if (other < difference)
            {
                back = true;
            }
            else
            {
                forward = true;
            }
        }

        if (other <= difference)
        {
            difference = other;
        }

        if (step >= difference)
        {
            ChangeDirection(goal);

            return default;
        }

        if (back)
        {
            ChangeDirection(new Angle((byte)(current.Value - step)));
        }

        if (forward)
        {
            ChangeDirection(new Angle((byte)(step + _direction.Value)));
        }

        return (byte)_direction.MeasureTurnTo(goal);
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

    /// <summary>
    /// Draws the sprite with the frames of its definition.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x0040ec40)]
    public virtual void Draw(Screen screen, Map map, Vector2 camera) => Definition.Draw(screen, map, this, camera);

    /// <summary>
    /// Returns the gamma of the sprite, made of its own gamma or its army's, plus the gamma of the current keyframe.
    /// </summary>
    /// <returns>The gamma.</returns>
    [EngineRoutine(0x004430b0)]
    public Gamma GetGamma()
    {
        var definition = Definition;
        var gamma = HasExtra && (GammaOverride.Negative != 0 || GammaOverride.Positive != 0) ? GammaOverride : definition.Gammas[(int)Army & SpriteDefinition.ArmyMask];
        if (!definition.HasColorKeyframes)
            return gamma;

        ref readonly var weapon = ref definition.Weapon;
        int blue = (int)weapon.Interpolate(weapon.BlueKeys, (int)KeyframePosition, KeyframePosition);
        int green = (int)weapon.Interpolate(weapon.GreenKeys, (int)KeyframePosition, KeyframePosition);
        int red = (int)weapon.Interpolate(weapon.RedKeys, (int)KeyframePosition, KeyframePosition);
        int alpha = Math.Clamp((int)weapon.Interpolate(weapon.AlphaKeys, (int)KeyframePosition, KeyframePosition), -byte.MaxValue, byte.MaxValue);
        var keyframe = alpha < 0 ? new Gamma((uint)(alpha * -(1 << GammaAlphaShift)), 0) : new Gamma(0, (uint)alpha << GammaAlphaShift);
        return gamma.Add(keyframe.WithRed(red).WithGreen(green).WithBlue(blue));
    }

    internal void ReplaceDefinition(SpriteDefinition definition) => Definition = definition;

    internal void SetAnimationValue(SpriteAnimation state) => Animation = state;

    [EngineRoutine(0x00440030)]
    internal void ChangeCoor(Vector3 position)
    {
        var delta = position - Position;
        Map.LowerHeights(Definition, this);
        Shift(delta);
        Map.RaiseHeights(Definition, this);

        foreach (var link in EnumerateLinks())
        {
            Map.LowerHeights(link.Definition, this);
            link.Shift(delta);
            Map.RaiseHeights(link.Definition, this);
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

            int count = Definition.ChildCounts[(int)SpriteAnimation.Fight];

            if (Map.Action(this, ActionCode.GetAmmo) < Math.Abs(count))
                return AttackResult.NoAmmo;

            Map.Action(this, ActionCode.AddAmmo, -count);

            ChangeAnimation(SpriteAnimation.Fight);
            Timer = Definition.ReloadTime + ReloadTimerOffset;

            return AttackResult.Attacked;
        }

        if (Command == Command.Attack)
            return AttackResult.Approach;

        return distance < Definition.DetectRange * 2 ? AttackResult.Pursue : AttackResult.GiveUp;
    }

    [EngineRoutine(0x0043fa00)]
    internal bool MoveTactMapLimit(Vector2 position, int elapsed)
    {
        var size = Map.Movement.MapSize;

        if (!(position.X >= 0))
        {
            RotateTact(Angle.East, elapsed);

            return true;
        }

        if (!(position.Y >= 0))
        {
            RotateTact(Angle.South, elapsed);

            return true;
        }

        if (!(position.X < size.Width))
        {
            RotateTact(Angle.West, elapsed);

            return true;
        }

        if (!(position.Y < size.Height))
        {
            RotateTact(Angle.North, elapsed);

            return true;
        }

        return false;
    }

    [EngineRoutine(0x0043f520)]
    internal virtual void Destruct()
    {
        Map.RunDestroyHandler(this);
        Map.LowerHeights(Definition, this);

        bool isEmpty = Definition == Map.Definitions.Empty;

        if (isEmpty && ReferenceCount != 0)
        {
            Map.Session.Logger.Error("SPRITE 0"u8, LogError.Invalid, 0);
        }

        if (ReferenceCount > 1)
        {
            Map.Collector.DeleteFromHash(this);

            if (ReferenceCount > 1)
            {
                Map.DeletePointerToSprite(this);
            }
        }

        int army = (int)Army & 3;

        if (Definition.Counts[army] != 0)
        {
            Definition.Counts[army]--;
        }

        Goal = null;

        while (Link is not null)
        {
            Map.DeleteSprite(Link);
        }

        Owner?.Link = null;

        if (!isEmpty)
        {
            Map.RemoveFromUpdates(this);
            Release();
        }

        if (ReferenceCount != 0)
        {
            Map.Session.Logger.Error("SPRITE 0"u8, LogError.Text, 0);
        }

        if (Handle == 0)
            return;

        Map.Handles.Remove(Handle);
        Handle = 0;
    }

    internal virtual int Action(int act, int var1, int var2, int var3, Sprite? target) => SpriteAction(act, var1, var2, var3, target);

    [EngineRoutine(0x0043f820)]
    internal virtual void MoveTact()
    {
        if (!Definition.CanMove)
            return;

        ref readonly var gameObject = ref Definition.Object;
        int elapsed = Map.FrameTime;
        var next = Map.Motion.CalculateNewCoordinates(this, elapsed);
        float current = Map.Movement.GetHeight(Position.AsVector2());
        float ground = Map.Movement.GetHeight(next.AsVector2());
        float hover = ground + gameObject.TopZ;

        if (gameObject.SpriteType.HasFlag(SpriteType.Cannon) && gameObject.Properties.HasFlag(ObjectProperties.Gravity) && ground >= next.Z && Position.Z >= current)
        {
            next.Z = Position.Z;
        }
        else
        {
            KeepHoverHeight(this, gameObject, hover, ref next);
        }

        if (HasGoal && !Speed.IsExactlyZero() && this is { HasReachedX: true, HasReachedY: true })
        {
            SpriteKinematics.Stop(this);
        }

        if (next.AsVector2() != Position.AsVector2())
        {
            if (Map.Projectiles.FindBlocker(this, next) is not null)
            {
                VerticalSpeed = 0;
                Speed = 0;
                IsBlocked = true;
            }
            else
            {
                ChangeCoor(next);
            }
        }

        if (!Position.Z.IsExactly(next.Z))
        {
            ChangeCoor(Position with { Z = next.Z });
        }
    }

    [EngineRoutine(0x0043f720)]
    internal virtual void DeletePointerToSprite(Sprite removed)
    {
        Link?.DeletePointerToSprite(removed);

        if (Goal == removed)
        {
            if (Animation == SpriteAnimation.Fight)
            {
                SpriteKinematics.SetCommand(this, Command.OneAttack, Map.ConstructGoal(removed.Position));
            }
            else
            {
                SpriteKinematics.SetCommand(this, Command.None);
            }
        }

        for (int i = 0; i < ActionStack.Count; i++)
        {
            var order = ActionStack[i];

            if (order.Target == removed && Act.HasSpriteTarget((ActionCode)order.Value))
            {
                ActionStack.Replace(i, order with { Value = Act.ForgottenOrder, Var1 = 0, Target = null });
            }
        }
    }

    [EngineRoutine(0x00442660)]
    internal virtual void DrawInfo()
    {
        var graphics = Map.Session.Graphics;
        var view = graphics.Viewport;
        float y = view.Top;
        var position = Position;
        int ammo = Map.Action(this, ActionCode.GetAmmo);
        graphics.DrawFormattedText(new Vector2(view.Left + InfoTextLeft, y), $"Ref={ReferenceCount,-3} cmd={(int)Command,1} ani={(int)Animation,-2} hp={Hp,-3} AT={(int)AttackResult} goal={GetDefinitionId(Goal),-3} spd={(int)(Speed * SpeedScale),-3},{(int)(VerticalSpeed * SpeedScale),-3} timer={Timer} ammo={ammo} mvE={(HasReachedX ? 1 : 0),1}{(HasReachedY ? 1 : 0),1} {(int)position.X},{(int)position.Y},{(int)position.Z}");

        var link = Link;
        if (link is not null && link.Definition == Definition.LinkDefinition)
        {
            y += InfoLineHeight;
            int linkAmmo = Map.Action(link, ActionCode.GetAmmo);
            graphics.DrawFormattedText(new Vector2(view.Left + InfoTextLeft, y), $"Ref={link.ReferenceCount,-3} cmd={(int)link.Command,1} ani={(int)link.Animation,-2} hp={link.Hp,-3} AT={(int)link.AttackResult} goal={GetDefinitionId(link.Goal),-3} timer={link.Timer} ammo={linkAmmo}");
        }

        if (ActionStack.Count != 0)
        {
            y += InfoLineHeight;
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"{ActionStack.Count} - ");
            for (int i = ActionStack.Count - 1; i >= 0; i--)
            {
                var order = ActionStack[i];
                text.Append(CultureInfo.InvariantCulture, $"{order.Value}({order.Var1},{order.Var2},{order.Var3}) ");
            }

            graphics.DrawText(new Vector2(view.Left + OrdersTextLeft, y), GameTextEncoding.Cyrillic.GetBytes(text.ToString()), Map.White);
        }

        DrawGoalLines();
    }

    [EngineRoutine(0x00442900)]
    internal virtual void DrawGoalLines()
    {
        var graphics = Map.Session.Graphics;

        if (Goal is not null)
        {
            var start = Map.GetScreenPoint(this);
            var end = Map.GetScreenPoint(Goal);
            graphics.DrawLine(Map, start, end, Map.Green);
        }

        if (Link?.Goal is not null)
        {
            var start = Map.GetScreenPoint(Link);
            var end = Map.GetScreenPoint(Link.Goal);
            graphics.DrawLine(Map, start, end, Map.Red);
        }
    }

    private protected static void KeepHoverHeight(Sprite sprite, in GameObject gameObject, float hover, ref Vector3 next)
    {
        if (sprite.Position.Z.IsExactly(next.Z) || gameObject.Properties.HasFlag(ObjectProperties.Gravity) || hover.IsExactlyZero())
            return;

        float height = hover;

        if (hover <= sprite.Position.Z)
        {
            if (sprite.Position.Z <= hover)
            {
                height = next.Z;

                if (!gameObject.Properties.HasFlag(ObjectProperties.SelfMoving))
                {
                    next.Z = height;
                    sprite.VerticalSpeed = 0;
                }
            }
            else if (next.Z < hover)
            {
                next.Z = height;
                sprite.VerticalSpeed = 0;
            }
        }
        else if (hover <= next.Z)
        {
            next.Z = height;
            sprite.VerticalSpeed = 0;
        }
    }

    [EngineRoutine(0x00444a00)]
    private protected void Unlink()
    {
        Link?.Owner = Owner;

        if (Owner is not null)
        {
            Owner.Link = Link;
            Owner = null;
        }

        Link = null;
    }

    private static int GetDefinitionId(Sprite? sprite) => sprite is null ? 0 : (int)sprite.Definition.Id;

    private void Shift(Vector3 delta)
    {
        if (SpriteCollector.IsHashed(Definition))
        {
            Map.Collector.ChangeCoor(this, Position.AsVector2() + delta.AsVector2());
        }

        uint stamp = Map.WallTime;

        if (HasExtra && PreviousPositionStamp != stamp)
        {
            PreviousPositionStamp = stamp;
            PreviousPosition = Position;
        }

        Position += delta;
    }

    private Angle GetShownDirection(Angle direction)
    {
        if (VerticalSpeed.IsExactlyZero() || !Definition.Object.Properties.HasFlag(ObjectProperties.VerticalDirection))
            return direction;

        return Angle.FromOffset(new Vector2(direction.Vector.X * Speed * VelocityScale, -(direction.Vector.Y * Speed + VerticalSpeed) * VelocityScale));
    }

    private void FaceOwner(int elapsed, bool idle)
    {
        if (Owner is not null && idle)
        {
            RotateTact(Owner.Direction, elapsed);
        }
    }

    [EngineRoutine(0x004463a0)]
    private Angle MeasureDirectionToGoal()
    {
        var offset = GoalPosition - Position;
        return Angle.Aim(new Point((int)offset.X, (int)offset.Y));
    }
}

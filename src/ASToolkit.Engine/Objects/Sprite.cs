using System.Buffers.Text;
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

    private const int ChildPairCount = 2;
    private const int SpawnScatter = 8;
    private const int CommandBias = 0x3c;
    private const int AimAttempts = 5;
    private const float AimRandomScale = 6.1037019E-05f;
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
    private const int CommandShift = 2;
    private const int ArmyShift = 11;

    private SpriteFlags _flags;
    private Angle _direction;

    /// <summary>
    /// Initializes a new instance of the <see cref="Sprite"/> class on a map, placing it, giving it its army, health and first state, and adding it to the map's update lists and spatial hash.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0043f200)]
    public Sprite(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
    {
        Map = map;
        ActionStack = new MyList<Act>(Map.Session.Error);
        _direction = direction;
        Vid = vid;
        FirstFrame = vid.GetFirstFrame(SpriteAnimation.Stand);
        Frame = FirstFrame;
        LastFrame = FirstFrame + vid.GetFramesPerDirection(SpriteAnimation.Stand) - 1;

        if (vid.HasTimers)
        {
            CreateExtraData();
        }

        if (vid.Properties.HasFlag(ObjectProperties.Noise))
        {
            position.X += SpawnScatter - Map.Random.Next() % (SpawnScatter * 2 + 1);
            position.Y += SpawnScatter - Map.Random.Next() % (SpawnScatter * 2 + 1);
        }

        if (vid.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            float ground = Map.GetGroundZForMoveTo(vid, position.AsVector2());
            position.Z = parent is null ? ground : position.Z - parent.Position.Z + ground;
        }

        Position = position;

        if (parent is not null)
        {
            Army = parent.Army;
        }
        else
        {
            var link = vid.LinkVid;
            Army = vid.WeaponIndex == 0 && link is not null ? link.Weapon.Army : vid.Weapon.Army;
        }

        CreatedAt = Map.CurrentTime;
        IsInvisible = vid.Properties.HasFlag(ObjectProperties.InvisibleForEnemy) && (int)Army != Map.ControlledPlayer;
        Hp = vid.ArmyMaxHp[(int)Army & Vid.ArmyMask];
        var state = Map.GetInitialAnimation(vid);
        Start(state, direction, Map.CurrentTime, Map.Random);

        if (vid != Map.EmptyVid)
        {
            AddRef();
            AddToUpdates();
        }

        CreateLink();
        Map.Sprites.InsertToHash(this);
        vid.LastCreatedAt = Map.RealCurrentTime;
        vid.Counts[(int)Army & 3]++;

        if (state != SpriteAnimation.Birth && !Map.IsLoading)
        {
            CreateChildFor(SpriteAnimation.Birth);
        }

        vid.SetGridZ(this);
    }

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
    /// Gets the shared data and frames of the sprite's object ID.
    /// </summary>
    public Vid Vid
    {
        [EngineRoutine(0x0043a850)]
        get;
        private set;
    }

    /// <summary>
    /// Gets or sets the speed of the sprite in pixels per millisecond.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Gets or sets the vertical speed of the sprite.
    /// </summary>
    public float VerticalSpeed { get; set; }

    /// <summary>
    /// Gets or sets the command the sprite carries out.
    /// </summary>
    public Command Command
    {
        get => (Command)((int)(_flags & SpriteFlags.Command) >> CommandShift);
        set => _flags = _flags & ~SpriteFlags.Command | (SpriteFlags)((int)value << CommandShift) & SpriteFlags.Command;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite moves toward its goal.
    /// </summary>
    public bool IsMoving
    {
        get => _flags.HasFlag(SpriteFlags.Moving);
        set => SetFlag(SpriteFlags.Moving, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sound of the current state was requested.
    /// </summary>
    public bool HasPlayedStateSound
    {
        get => _flags.HasFlag(SpriteFlags.PlayedStateSound);
        set => SetFlag(SpriteFlags.PlayedStateSound, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite's last move was blocked.
    /// </summary>
    public bool IsBlocked
    {
        get => _flags.HasFlag(SpriteFlags.Blocked);
        set => SetFlag(SpriteFlags.Blocked, value);
    }

    /// <summary>
    /// Gets or sets the army of the sprite.
    /// </summary>
    public Army Army
    {
        get => (Army)((int)(_flags & SpriteFlags.Army) >> ArmyShift);
        set => _flags = _flags & ~SpriteFlags.Army | (SpriteFlags)((int)value << ArmyShift) & SpriteFlags.Army;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the first of a mirrored pair of children launched and the second is still due.
    /// </summary>
    public bool IsAwaitingSecondPulse
    {
        get => _flags.HasFlag(SpriteFlags.AwaitingSecondPulse);
        set => SetFlag(SpriteFlags.AwaitingSecondPulse, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite has passed its goal's x coordinate.
    /// </summary>
    public bool HasReachedX
    {
        get => _flags.HasFlag(SpriteFlags.ReachedX);
        set => SetFlag(SpriteFlags.ReachedX, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite has passed its goal's y coordinate.
    /// </summary>
    public bool HasReachedY
    {
        get => _flags.HasFlag(SpriteFlags.ReachedY);
        set => SetFlag(SpriteFlags.ReachedY, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is hidden.
    /// </summary>
    public bool IsInvisible
    {
        get => _flags.HasFlag(SpriteFlags.Invisible);
        set => SetFlag(SpriteFlags.Invisible, value);
    }

    /// <summary>
    /// Gets the number of lists and sprites that hold this sprite.
    /// </summary>
    public int ReferenceCount { get; private set; }

    /// <summary>
    /// Gets or sets the position of the sprite.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the sprite this sprite moves toward or attacks, and holds a reference to it.
    /// </summary>
    public Sprite? Goal
    {
        [EngineRoutine(0x004429e0)]
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
                        Map.Session.Error.Error($"SPRITE {(int)previous.Vid.Id}", LogError.Invalid, count, "noRef at Release"u8);
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
    /// Gets or sets the next sprite of this sprite's link chain.
    /// </summary>
    public Sprite? Link
    {
        [EngineRoutine(0x0043f180)]
        get;
        set;
    }

    /// <summary>
    /// Gets or sets the sprite this sprite is linked to.
    /// </summary>
    public Sprite? Owner { get; set; }

    /// <summary>
    /// Gets the state of the sprite.
    /// </summary>
    public SpriteAnimation Animation { get; private set; }

    /// <summary>
    /// Gets the direction the sprite faces.
    /// </summary>
    public Angle Direction
    {
        [EngineRoutine(0x0043a840)]
        get => _direction;
        init => _direction = value;
    }

    /// <summary>
    /// Gets or sets the sprite's countdown timer in milliseconds.
    /// </summary>
    public int Timer { get; set; }

    /// <summary>
    /// Gets or sets the health of the sprite.
    /// </summary>
    public int Hp { get; set; }

    /// <summary>
    /// Gets the order stack of the sprite.
    /// </summary>
    public MyList<Act> ActionStack { get; }

    /// <summary>
    /// Gets a value indicating whether the sprite carries extra data (lifetime, keyframe track, stream period and top speed of its own).
    /// </summary>
    public bool HasExtraData => ExtraData is not null;

    /// <summary>
    /// Gets the position the sprite had before its first move of the frame.
    /// </summary>
    public Vector3 PreviousPosition => ExtraData?.PreviousPosition ?? default;

    /// <summary>
    /// Gets the real time of the frame when <see cref="PreviousPosition"/> was saved.
    /// </summary>
    public uint PreviousPositionRealTime => ExtraData?.PreviousPositionRealTime ?? 0;

    /// <summary>
    /// Gets or sets the time the sprite has left before it dies.
    /// </summary>
    public int Lifetime
    {
        get => ExtraData?.Lifetime ?? Vid.Unlimited;
        set
        {
            EnsureExtraData();
            ExtraData!.Lifetime = value;
        }
    }

    /// <summary>
    /// Gets or sets the period of the sprite's stream children.
    /// </summary>
    public int StreamPeriod
    {
        get => ExtraData?.StreamPeriod ?? 1;
        set => ExtraData!.StreamPeriod = value;
    }

    /// <summary>
    /// Gets or sets the time the sprite's keyframe track started.
    /// </summary>
    public int KeyframeStart
    {
        get => ExtraData?.KeyframeStart ?? 0;
        set => ExtraData!.KeyframeStart = value;
    }

    /// <summary>
    /// Gets or sets the position along the sprite's keyframe track.
    /// </summary>
    public float KeyframePosition
    {
        get => ExtraData?.KeyframePosition ?? 0;
        set => ExtraData!.KeyframePosition = value;
    }

    /// <summary>
    /// Gets or sets the top speed of the sprite in pixels per millisecond.
    /// </summary>
    public float MaxSpeed
    {
        get => ExtraData?.MaxSpeed ?? Vid.Speed;
        set
        {
            EnsureExtraData();
            ExtraData!.MaxSpeed = value;
        }
    }

    /// <summary>
    /// Gets the gamma applied to the sprite while it is drawn, or the default gamma when there isn't one.
    /// </summary>
    public Gamma GammaOverride => ExtraData?.GammaOverride ?? default;

    /// <summary>
    /// Gets the items the sprite carries.
    /// </summary>
    public IReadOnlyList<int> Items => ExtraData?.Items ?? [];

    /// <summary>
    /// Gets or sets the sprite that fired this sprite.
    /// </summary>
    public Sprite? Shooter { get; set; }

    /// <summary>
    /// Gets or sets the script handle of the sprite, or 0 if the sprite doesn't have one.
    /// </summary>
    public int Handle { get; set; }

    /// <summary>
    /// Gets the weapon slot of the torso linked by the sprite's object ID.
    /// </summary>
    public int WeaponSlot => (Vid.LinkVid?.Id ?? 0) - KnownObject.SecretWeaponTorso;

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
    /// Gets a value indicating whether the sprite is between attacks.
    /// </summary>
    public bool IsReloading => Timer > ReloadTimerOffset || Animation == SpriteAnimation.Fight && Frame <= LastFrame;

    /// <summary>
    /// Gets this sprite's link if it's the armed object linked by the sprite's object ID; otherwise, <see langword="null"/>.
    /// </summary>
    public Sprite? ArmedLink => HasLink && Link!.Vid.CanFight() ? Link : null;

    /// <summary>
    /// Gets a value indicating whether this sprite's link is the object linked by the sprite's object ID.
    /// </summary>
    public bool HasLink
    {
        [EngineRoutine(0x0044e1b0)]
        get => Link is not null && Link.Vid == Vid.LinkVid;
    }

    internal bool IsBuiltByDepo
    {
        get => _flags.HasFlag(SpriteFlags.BuiltByDepo);
        set => SetFlag(SpriteFlags.BuiltByDepo, value);
    }

    internal bool IsAttackReported
    {
        get => _flags.HasFlag(SpriteFlags.AttackReported);
        set => SetFlag(SpriteFlags.AttackReported, value);
    }

    internal ExtraSpriteData? ExtraData { get; private set; }

    internal Map Map { get; }

    internal AttackResult AttackResult { get; set; }

    private static ReadOnlySpan<byte> ItemMark => [1];

    private static ReadOnlySpan<byte> CommandEnd => ";"u8;

    /// <summary>
    /// Draws the sprite with the frames of its object ID.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    [EngineRoutine(0x0040ec40)]
    public virtual void Draw(Screen screen) => Vid.Draw(screen, this);

    /// <summary>
    /// Determines whether this sprite touches a target on the ground plane.
    /// </summary>
    /// <param name="target">The center of the target.</param>
    /// <param name="targetHalf">Half the width and depth of the target.</param>
    /// <returns><see langword="true"/> if this sprite touches the target; otherwise, <see langword="false"/>.</returns>
    public bool IsTouching(Vector2 target, Vector2 targetHalf)
    {
        var reach = Vid.Half + targetHalf;

        return Math.Abs(Position.X - target.X) < reach.X && Math.Abs(Position.Y - target.Y) < reach.Y;
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
        var layout = Vid;
        bool firstFrame = Vid.Properties.HasFlag(ObjectProperties.OnePhase);
        LastTickTime = time - (firstFrame ? 0 : random.Next(Vid.SpriteSpeed + 1));
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
            var linkVid = link.Vid;
            bool fixedOffset = linkVid.Properties.HasFlag(ObjectProperties.FixedOffset);
            var offset = Vid.LinkOffset;

            if (link.Vid == Vid.LinkVid && !fixedOffset && (!offset.X.IsExactlyZero() || !offset.Y.IsExactlyZero()))
            {
                var linkOffset = Vid.GetLinkOffset(link.Vid, Vid.GetFrameDirection(shown));
                link.ChangeCoor(new Vector3(linkOffset.AsVector2() + Position.AsVector2(), link.Position.Z));
            }
            else if (linkVid.Class == SpriteClass.Linker && !fixedOffset)
            {
                ((Linker)link).LinkRotate(Vid.GetFrameDirection(shown));
            }

            if (link.Vid.RotationRate.IsExactlyZero())
            {
                link.ChangeDirection(direction);
            }
        }

        var layout = Vid;

        if (layout.Directions != 1)
        {
            int frameOffset = Frame - FirstFrame;
            FirstFrame = layout.GetFirstFrame(Animation) + layout.RealDirection(shown) * layout.GetFramesPerDirection(Animation);
            LastFrame = FirstFrame + layout.GetFramesPerDirection(Animation) - 1;
            Frame = FirstFrame + frameOffset;
        }

        _direction = direction;
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
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Invalid, (int)state, "new_animation in ChangeAnimation"u8);

            return;
        }

        var link = Link;

        if (link is not null && link.Vid == Vid.LinkVid && (state >= SpriteAnimation.Death || link.Vid.WeaponIndex == 0 &&
            !link.Vid.Properties.HasFlag(ObjectProperties.Wind) && link.Animation is not SpriteAnimation.Fight and < SpriteAnimation.Death))
        {
            link.ChangeAnimation(state);
        }

        if (Animation == state)
            return;

        HasPlayedStateSound = false;
        var layout = Vid;
        var direction = GetShownDirection(_direction);

        FirstFrame = layout.GetFirstFrame(state) + layout.RealDirection(direction) * layout.GetFramesPerDirection(state);
        Frame = FirstFrame;
        Animation = state;
        LastFrame = state > SpriteAnimation.Unload && layout.GetFrameCount(state) == 0
            ? FirstFrame : FirstFrame + layout.GetFramesPerDirection(state) - 1;
    }

    /// <summary>
    /// Sets the gamma applied to the sprite while it is drawn.
    /// </summary>
    /// <param name="gamma">The gamma.</param>
    [EngineRoutine(0x00443070)]
    public void SetGamma(Gamma gamma)
    {
        EnsureExtraData();
        ExtraData!.GammaOverride = gamma;
    }

    /// <summary>
    /// Returns the gamma of the sprite, made of its own gamma or its army's, plus the gamma of the current keyframe.
    /// </summary>
    /// <returns>The gamma.</returns>
    [EngineRoutine(0x004430b0)]
    public Gamma GetGamma()
    {
        var gamma = HasExtraData && (GammaOverride.Negative != 0 || GammaOverride.Positive != 0) ? GammaOverride : Vid.Gammas[(int)Army & Vid.ArmyMask];
        if (!Vid.HasColorKeyframes)
            return gamma;

        ref readonly var weapon = ref Vid.Weapon;
        int blue = (int)weapon.Interpolate(weapon.BlueKeys, (int)KeyframePosition, KeyframePosition);
        int green = (int)weapon.Interpolate(weapon.GreenKeys, (int)KeyframePosition, KeyframePosition);
        int red = (int)weapon.Interpolate(weapon.RedKeys, (int)KeyframePosition, KeyframePosition);
        int alpha = Math.Clamp((int)weapon.Interpolate(weapon.AlphaKeys, (int)KeyframePosition, KeyframePosition), -byte.MaxValue, byte.MaxValue);
        var keyframe = alpha < 0 ? new Gamma((uint)(alpha * -(1 << GammaAlphaShift)), 0) : new Gamma(0, (uint)alpha << GammaAlphaShift);
        return gamma.Add(keyframe.WithRed(red).WithGreen(green).WithBlue(blue));
    }

    /// <summary>
    /// Returns the health of this sprite, or of its armed link, scaled from 0 to 255 of the maximum.
    /// </summary>
    /// <returns>The scaled health.</returns>
    [EngineRoutine(0x00444360)]
    public int PercentHp()
    {
        var link = ArmedLink;

        if (link is not null)
        {
            int linkMax = link.Vid.ArmyMaxHp[(int)link.Army & Vid.ArmyMask];

            if (linkMax != 0)
                return link.Hp * HpShare / linkMax;
        }

        int max = Vid.ArmyMaxHp[(int)Army & Vid.ArmyMask];

        return max != 0 ? Hp * HpShare / max : 0;
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

        float rate = Vid.RotationRate;
        if (rate.IsExactly(Vid.Unlimited))
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
    /// Plays a sound from a sprite's position on the screen.
    /// </summary>
    /// <param name="id">The sound ID.</param>
    [EngineRoutine(0x00445470)]
    public void PlaySfx(int id)
    {
        Map.Sound.PlayAt(id, Position.ToDrawPosition() - Map.Shift - Map.Session.Graphics.ScreenSize.ToVector2() * Map.HalfShare);
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

        if (Vid.FightChild != 0 && Vid.WeaponIndex != 0 && (Vid.Weapon.Targets & target.Vid.SpriteType) != 0)
            return true;

        var link = ArmedLink;

        return link is not null && (link.Vid.Weapon.Targets & target.Vid.SpriteType) != 0;
    }

    /// <summary>
    /// Creates the child objects that the sprite's object ID lists for a state.
    /// </summary>
    /// <param name="state">The state whose children to create.</param>
    [EngineRoutine(0x00446430)]
    public void CreateChildFor(SpriteAnimation state)
    {
        if (Vid.ChildVids[(int)state] is not null)
        {
            CreateChildAs(state);
        }

        int sound = Vid.Sounds[(int)state];

        if (sound != 0)
        {
            PlaySfx(sound);
        }
    }

    /// <summary>
    /// Adds an item at the end of the sprite's items.
    /// </summary>
    /// <param name="item">The item.</param>
    [EngineRoutine(0x00446760)]
    public void InsertItem(int item)
    {
        EnsureExtraData();

        ExtraData!.Items.Insert(item);
    }

    /// <summary>
    /// Creates the sprite's extra data if it does not exist yet.
    /// </summary>
    public void EnsureExtraData()
    {
        if (!HasExtraData)
        {
            CreateExtraData();
        }
    }

    /// <summary>
    /// Creates the sprite's extra data, giving it its own lifetime, keyframe track, stream period and top speed.
    /// </summary>
    public void CreateExtraData() => ExtraData = new ExtraSpriteData(this);

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
    public int GetItemNumber(int item) => ExtraData?.Items.Location(item) ?? -1;

    /// <summary>
    /// Returns the direction from this sprite to another sprite.
    /// </summary>
    /// <param name="other">The other sprite.</param>
    /// <returns>The direction.</returns>
    [EngineRoutine(0x004474b0)]
    public Angle DirectionTo(Sprite other) => Angle.FromOffset((other.Position - Position).AsVector2());

    [EngineRoutine(0x0043f520)]
    internal virtual void Destruct()
    {
        Map.RunDestroyHandler(this);
        Vid.ResetGridZ(this);

        bool isEmpty = Vid == Map.EmptyVid;

        if (isEmpty && ReferenceCount != 0)
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Invalid, ReferenceCount, "noRef for SPRITE with EmptyVid"u8);
        }

        if (ReferenceCount > 1)
        {
            Map.Sprites.DeleteFromHash(this);

            if (ReferenceCount > 1)
            {
                Map.DeletePointerToSprite(this);
            }
        }

        int army = (int)Army & 3;

        if (Vid.Counts[army] != 0)
        {
            Vid.Counts[army]--;
        }

        Goal = null;

        while (Link is not null)
        {
            Map.DeleteSprite(Link);
        }

        Owner?.Link = null;

        if (!isEmpty)
        {
            RemoveFromUpdates();
            Release();
        }

        if (ReferenceCount != 0)
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Text, ReferenceCount, "Reference count non zero after delete"u8);
        }

        if (Handle == 0)
            return;

        Map.Handles.Remove(Handle);
        Handle = 0;
    }

    [EngineRoutine(0x0043f720)]
    internal virtual void DeletePointerToSprite(Sprite removed)
    {
        Link?.DeletePointerToSprite(removed);

        if (Goal == removed)
        {
            if (Animation == SpriteAnimation.Fight)
            {
                SetCommand(Command.OneAttack, Map.ConstructGoal(removed.Position));
            }
            else
            {
                SetCommand(Command.None);
            }
        }

        for (int i = 0; i < ActionStack.Count; i++)
        {
            var order = ActionStack[i];

            if (removed.Handle != 0 && order.Var1 == removed.Handle && Act.HasSpriteTarget((ActionCode)order.Value))
            {
                ActionStack[i] = order with { Value = Act.ForgottenOrder, Var1 = 0 };
            }
        }
    }

    [EngineRoutine(0x0043f820)]
    internal virtual void MoveTact()
    {
        if (!Vid.CanMove)
            return;

        var vid = Vid;
        int elapsed = Map.FrameTime;
        var next = MoveTactCalculateCoordinates(elapsed);
        float current = Map.GetGroundZ(Position.AsVector2());
        float ground = Map.GetGroundZ(next.AsVector2());
        float hover = ground + vid.TopZ;

        if (vid.SpriteType.HasFlag(OldSpriteType.Cannon) && vid.Properties.HasFlag(ObjectProperties.Gravity) && ground >= next.Z && Position.Z >= current)
        {
            next.Z = Position.Z;
        }
        else
        {
            KeepHoverHeight(this, vid, hover, ref next);
        }

        if (HasGoal && !Speed.IsExactlyZero() && this is { HasReachedX: true, HasReachedY: true })
        {
            Stop();
        }

        if (next.AsVector2() != Position.AsVector2())
        {
            if (CanPlaceWithCrush(next) is not null)
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

    [EngineRoutine(0x0043fa00)]
    internal bool MoveTactMapLimit(Vector2 position, int elapsed)
    {
        var size = Map.Size;

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

    [EngineRoutine(0x00440030)]
    internal void ChangeCoor(Vector3 position)
    {
        var delta = position - Position;
        for (var sprite = this; sprite is not null; sprite = sprite.Link)
        {
            sprite.Vid.ResetGridZ(this);

            if (SpriteCollector.IsHashed(sprite.Vid))
            {
                Map.Sprites.ChangeCoor(sprite, sprite.Position.AsVector2() + delta.AsVector2());
            }

            uint realTime = Map.RealCurrentTime;

            if (sprite.ExtraData is not null && sprite.ExtraData.PreviousPositionRealTime != realTime)
            {
                sprite.ExtraData.PreviousPositionRealTime = realTime;
                sprite.ExtraData.PreviousPosition = sprite.Position;
            }

            sprite.Position += delta;
            sprite.Vid.SetGridZ(this);
        }
    }

    [EngineRoutine(0x00442660)]
    internal virtual void DrawSecondaryInfo()
    {
        var graphics = Map.Session.Graphics;
        var view = graphics.Viewport;
        float y = view.Top;
        var position = Position;
        int ammo = Map.Action(this, ActionCode.GetAmmo);
        graphics.DrawFormattedText(new Vector2(view.Left + InfoTextLeft, y), $"Ref={ReferenceCount,-3} cmd={(int)Command,1} ani={(int)Animation,-2} hp={Hp,-3} AT={(int)AttackResult} goal={(Goal is null ? 0 : (int)Goal.Vid.Id),-3} spd={(int)(Speed * SpeedScale),-3},{(int)(VerticalSpeed * SpeedScale),-3} timer={Timer} ammo={ammo} mvE={(HasReachedX ? 1 : 0),1}{(HasReachedY ? 1 : 0),1} {(int)position.X},{(int)position.Y},{(int)position.Z}");

        var link = Link;
        if (link is not null && link.Vid == Vid.LinkVid)
        {
            y += InfoLineHeight;
            int linkAmmo = Map.Action(link, ActionCode.GetAmmo);
            graphics.DrawFormattedText(new Vector2(view.Left + InfoTextLeft, y), $"Ref={link.ReferenceCount,-3} cmd={(int)link.Command,1} ani={(int)link.Animation,-2} hp={link.Hp,-3} AT={(int)link.AttackResult} goal={(link.Goal is null ? 0 : (int)link.Goal.Vid.Id),-3} timer={link.Timer} ammo={linkAmmo}");
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

        DrawGoalLine();
    }

    [EngineRoutine(0x00442900)]
    internal virtual void DrawGoalLine()
    {
        var graphics = Map.Session.Graphics;

        if (Goal is not null)
        {
            var start = Map.GetScreenPoint(this);
            var end = Map.GetScreenPoint(Goal);
            graphics.WuLine(start, end, Map.Green);
        }

        if (Link?.Goal is not null)
        {
            var start = Map.GetScreenPoint(Link);
            var end = Map.GetScreenPoint(Link.Goal);
            graphics.WuLine(start, end, Map.Red);
        }
    }

    [EngineRoutine(0x004429f0)]
    internal bool RemoveFromUpdates()
    {
        Link?.RemoveFromUpdates();

        return Map.RemoveFromLayer(this);
    }

    [EngineRoutine(0x00442a10)]
    internal void AddToUpdates()
    {
        Link?.AddToUpdates();

        Map.AddToLayer(this);
    }

    internal void ReplaceVid(Vid vid) => Vid = vid;

    internal void ChangeZCoor(float z) => ChangeCoor(Position with { Z = z });

    [EngineRoutine(0x00444970)]
    internal int AddLink(Sprite? link)
    {
        if (link is null || link.Owner is not null)
            return 1;

        var previous = Link;

        if (previous is not null)
        {
            previous.Owner = null;
            link.AddLinkToLast(previous);
        }

        Link = link;
        link.Owner = this;

        return 0;
    }

    [EngineRoutine(0x004449c0)]
    internal void AddLinkToLast(Sprite link)
    {
        if (link.Owner is not null)
            return;

        var last = this;
        while (last.Link is not null)
        {
            last = last.Link;
        }

        last.Link = link;
        link.Owner = last;
    }

    [EngineRoutine(0x00444a30)]
    internal void DestroyLink(Vid? vid)
    {
        var previous = this;
        var link = Link;
        while (link is not null && link.Vid != vid)
        {
            previous = link;
            link = link.Link;
        }

        if (link is null)
            return;

        var next = link.Link;
        previous.Link = next;

        next?.Owner = previous;

        link.Link = null;
        link.Owner = null;
        Map.DeleteSprite(link);
    }

    [EngineRoutine(0x00444dd0)]
    internal void CreateChild()
    {
        var vid = Vid;
        var state = Animation;
        int slot = (int)state;
        var child = vid.ChildVids[slot];
        int count = Math.Abs(vid.ChildCounts[slot]);
        int i = 0;

        if (child is null || child.NotCreatedAsChild != 0)
            return;

        var offsetDirection = child.Properties.HasFlag(ObjectProperties.FixedOffset) ? default : vid.GetFrameDirection(Direction);

        if (count == ChildPairCount && vid.HasDoubleAttack)
        {
            if (IsAwaitingSecondPulse)
            {
                i = 1;
                HasPlayedStateSound = false;
            }
            else
            {
                count = 1;
            }

            IsAwaitingSecondPulse = !IsAwaitingSecondPulse;
        }

        for (; i < count; i++)
        {
            float childX = vid.ChildX[slot];
            float childY = vid.ChildY[slot];
            Vector2 shift;
            Vector2 lateral;

            if (vid.Children[slot] < 0 && vid.Class == SpriteClass.Region && childX.IsExactlyZero() && childY.IsExactlyZero())
            {
                lateral = Vector2.Zero;

                var region = this as Region;
                if (region is null || !region.Flags.HasFlag(RegionFlags.CoversMap))
                {
                    var regionSize = region?.Size ?? Vector2.Zero;
                    float regionSpreadX = regionSize.X * 0.5f - Map.Random.Next() * regionSize.X * GameRandom.UnitScale;
                    float regionSpreadY = regionSize.Y * 0.5f - Map.Random.Next() * regionSize.Y * GameRandom.UnitScale + Position.Z;
                    shift = new Vector2(regionSpreadX, regionSpreadY);
                }
                else
                {
                    float mapX = Map.Random.Next() * Map.Size.Width * GameRandom.UnitScale - Position.X;
                    float mapY = Map.Random.Next() * Map.Size.Height * GameRandom.UnitScale - Position.Y + Position.Z;
                    shift = new Vector2(mapX, mapY);
                }
            }
            else if (vid.Children[slot] < 0)
            {
                float spreadX = childX + childX;
                spreadX = childX - Map.Random.Next() * spreadX * GameRandom.UnitScale;
                float spreadY = childY + childY;
                spreadY = childY - Map.Random.Next() * spreadY * GameRandom.UnitScale;
                lateral = new Vector2(-(offsetDirection.Cosine * spreadX), -(offsetDirection.ProjectedSine * spreadX));
                shift = new Vector2(offsetDirection.Sine * spreadY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * spreadY);
            }
            else if (i == 1)
            {
                lateral = new Vector2(-(offsetDirection.Cosine * childX), -(offsetDirection.ProjectedSine * childX));
                shift = new Vector2(offsetDirection.Sine * childY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * childY);
            }
            else if (i == 2)
            {
                lateral = Vector2.Zero;
                shift = new Vector2(offsetDirection.Sine * childY, offsetDirection.ProjectedCosine * childY);
            }
            else
            {
                lateral = new Vector2(offsetDirection.Cosine * childX, offsetDirection.ProjectedSine * childX);
                shift = new Vector2(offsetDirection.Sine * childY + lateral.X, lateral.Y - offsetDirection.ProjectedCosine * childY);
            }

            var childPosition = new Vector3(shift.X + Position.X, shift.Y + Position.Y, vid.ChildZ[slot] + Position.Z);

            if (child.Class == SpriteClass.Unit && Map.Sprites.CanPlace(child, childPosition) is not null || state == SpriteAnimation.Fight && Goal is null)
                continue;

            var childDirection = child.Properties.HasFlag(ObjectProperties.RandomBirth) ? new Angle((byte)(Map.Random.Next() & byte.MaxValue)) : Direction;
            var created = Map.CreateSprite(child, childPosition, childDirection, this);

            var goal = Goal;

            if (created is null || goal is null || state != SpriteAnimation.Fight)
                continue;

            if (vid.Weapon.Flags.HasFlag(WeaponFlags.NoSpread) && goal.Vid != Map.EmptyVid)
            {
                created.Attack(goal);
                created.StartMove();
            }
            else
            {
                var aimPoint = goal.Position;

                for (int attempt = 0; attempt < AimAttempts; attempt++)
                {
                    float pointX = vid.Weapon.Aim + goal.Position.X + lateral.X - Map.Random.Next() * vid.Weapon.Aim * AimRandomScale;
                    float pointY = vid.Weapon.Aim + goal.Position.Y + lateral.Y - Map.Random.Next() * vid.Weapon.Aim * AimRandomScale;
                    aimPoint = new Vector3(pointX, pointY, goal.Position.Z);

                    if (Map.GetGroundZ(new Vector2(pointX, pointY)) < goal.Position.Z && Map.GetGroundZ((goal.Position.AsVector2() + aimPoint.AsVector2()) * 0.5f) < goal.Position.Z)
                        break;
                }

                var aimed = Map.ConstructGoal(aimPoint);
                created.Attack(aimed);
                created.StartMove();
            }
        }

        if (state == SpriteAnimation.Fight && !IsAwaitingSecondPulse && Command is Command.OneAttack or Command.DistAttack &&
            (!vid.Properties.HasFlag(ObjectProperties.Track) && !child.Properties.HasFlag(ObjectProperties.BirthAsSmoke) || Frame == LastFrame))
        {
            var owner = Owner;

            if (Goal is not null && owner is not null && owner.Goal == Goal)
            {
                if (owner.Command is Command.OneAttack or Command.DistAttack)
                {
                    owner.SetCommand(Command.None);
                }

                if (owner.Vid.Class == SpriteClass.Engine && owner.Command == Command.EngineOneAttack)
                {
                    ((Engine)owner).SetCommandToTrain(Command.EngineOneAttackDone, null, null, null);
                }
            }

            SetCommand(Command.None);
        }
    }

    [EngineRoutine(0x004454d0)]
    internal void ChangeHp(int hp)
    {
        if (hp < 1 && Vid.MaxHp != 0)
        {
            if (Animation < SpriteAnimation.Death)
            {
                int army = (int)Army & 3;
                Vid.Killed[army]++;
                ChangeAnimation(Vid.ArmyMaxHp[army] < Hp - hp && Vid.ChildVids[(int)SpriteAnimation.Death2] is not null ? SpriteAnimation.Death2 : SpriteAnimation.Death);
            }

            return;
        }

        int half = Vid.ArmyMaxHp[(int)Army & 3] / 2;
        if (half < hp && Hp <= half)
        {
            DestroyLink(Vid.ChildVids[(int)SpriteAnimation.Wound]);
        }

        if (hp <= half && half < Hp)
        {
            ChangeAnimation(SpriteAnimation.Wound);
        }

        Hp = hp;
    }

    [EngineRoutine(0x004455c0)]
    internal void DrawSpriteBox()
    {
        var vid = Vid;

        if (vid.Class == SpriteClass.Region)
        {
            DrawSecondaryInfo();

            return;
        }

        var type = vid.SpriteType;
        uint color;

        if (type.HasFlag(OldSpriteType.Terrain) && vid.Properties.HasFlag(ObjectProperties.Hash))
        {
            color = Map.Black;
        }
        else if (vid.Layer == ObjectLayer.Alpha)
        {
            color = Map.White;
        }
        else if (type.HasFlag(OldSpriteType.Unit))
        {
            color = Map.Green;
        }
        else if (type.HasFlag(OldSpriteType.Avia))
        {
            color = Map.LightBlue;
        }
        else if (type.HasFlag(OldSpriteType.Object))
        {
            color = Map.Yellow;
        }
        else if (type.HasFlag(OldSpriteType.Railway))
        {
            color = Map.Red;
        }
        else if (type.HasFlag(OldSpriteType.Cannon))
        {
            color = Map.Gray;
        }
        else if (vid.Class == SpriteClass.Frame)
        {
            color = Map.White;
        }
        else
        {
            color = Map.Red;
        }

        var position = Position;
        float bottom = position.Y - position.Z + vid.Half.Y - Map.Shift.Y;
        float right = vid.Half.X + position.X - Map.Shift.X;
        float top = position.Y - position.Z - vid.Half.Y - Map.Shift.Y;
        float left = position.X - vid.Half.X - Map.Shift.X;
        Map.Session.Graphics.Box(new Vector2(left, top), new Vector2(right, bottom), color);

        if (type.HasFlag(OldSpriteType.Object) || type.HasFlag(OldSpriteType.Unit))
        {
            float feet = position.Y - position.Z - Map.Shift.Y;
            float roof = position.Y - position.Z - vid.Size.Z - Map.Shift.Y;
            float x = position.X - Map.Shift.X;
            Map.Session.Graphics.WuLine(new Vector2(x, roof), new Vector2(x, feet), Map.Blue);
        }

        Map.Session.Graphics.DrawFormattedText(new Vector2(position.X - Map.Shift.X + Map.TextLineOffset, position.Y - position.Z - Map.Shift.Y), $"{(int)vid.Id}");
    }

    [EngineRoutine(0x00445770)]
    internal void CreateLink()
    {
        var vid = Vid.LinkVid;

        if (vid is null || Link?.Vid == vid || vid.NotCreatedAsChild != 0)
            return;

        var position = Position + Vid.GetLinkOffset(vid, Direction);
        var link = Map.CreateSprite(vid, position, Direction);
        AddLink(link);

        if (Link is null)
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Create, 0, "link"u8);

            return;
        }

        Link.ChangeArmy((int)Army);
    }

    [EngineRoutine(0x004458a0)]
    internal ReadOnlyMemory<byte> GetTextActions()
    {
        var text = new List<byte>();
        foreach (var order in ActionStack.Items)
        {
            text.Add(unchecked((byte)(order.Value + CommandBias)));
            AppendNumber(text, order.Var1);
            text.Add((byte)',');
            AppendNumber(text, order.Var2);
            text.Add((byte)',');
            AppendNumber(text, order.Var3);
            text.AddRange(CommandEnd);
        }

        return text.ToArray();
    }

    [EngineRoutine(0x00445990)]
    internal void SetTextActions(ReadOnlyMemory<byte> text)
    {
        int var1 = 0, var2 = 0, var3 = 0;
        while (!text.IsEmpty)
        {
            var rest = text.Span;
            byte command = rest[0];
            int i = 1;
            if (CRuntime.TryScanInteger(rest[i..], out int scanned, out int length))
            {
                var1 = scanned;
                i += length;
                if (i < rest.Length && rest[i] == ',' && CRuntime.TryScanInteger(rest[(i + 1)..], out scanned, out length))
                {
                    var2 = scanned;
                    i += 1 + length;
                    if (i < rest.Length && rest[i] == ',' && CRuntime.TryScanInteger(rest[(i + 1)..], out scanned))
                    {
                        var3 = scanned;
                    }
                }
            }

            ActionStack.Insert(new Act(command - CommandBias, var1, var2, var3));
            text = text.After(CommandEnd);
        }
    }

    [EngineRoutine(0x00445b10)]
    internal void AddActionAfterStop(int act, int var1, int var2, int var3)
    {
        var order = new Act(act, var1, var2, var3);
        var orders = ActionStack.Items;
        for (int i = orders.Length - 1; i >= 0; i--)
        {
            if (orders[i] is { Value: Act.StopMarker, Var1: 0, Var2: 0, Var3: 0 })
            {
                ActionStack.InsertBefore(i + 1, order);

                return;
            }
        }

        ActionStack.InsertFirst(order);
    }

    [EngineRoutine(0x00445bb0)]
    internal bool ActionStackHaveCommand(ActionCode command)
    {
        foreach (var act in ActionStack.Items)
        {
            if (act.Value == (int)command)
                return true;
        }

        return false;
    }

    [EngineRoutine(0x00445fb0)]
    internal void Attack(Sprite? target)
    {
        var vid = Vid;

        if (vid.Class == SpriteClass.Linker && vid.FightChild != 0)
        {
            var state = Animation;
            SetAnimationValue(SpriteAnimation.Fight);
            SetCommand(Command.OneAttack, target);
            CreateChild();
            SetAnimationValue(state);
            SetCommand(Command.None);

            return;
        }

        // NOTE: Trains are left unported; the IsTrain action of the attack test answers for every class
        if (target is null || target.Vid == Map.EmptyVid || CanAttackThisSprite(target) || vid.Class == SpriteClass.Cannon ||
            Map.Action(this, ActionCode.IsTrain) != 0)
        {
            SetCommand(Command.Attack, target);
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

        if (Vid.FightChild == 0 || Vid.WeaponIndex == 0)
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

        float distance = Position.AsVector2().NearLength(GoalPosition.AsVector2());
        bool inRange = Owner is null || Command == Command.DistAttack || distance < Vid.Weapon.BattleRange && distance > Vid.Weapon.MinRange;
        bool aimed = inRange && (RotateTact(MeasureDirectionToGoal(), elapsed) == default || Vid.CanAttackWhileTurning);

        if (!inRange)
        {
            FaceOwner(elapsed, true);
        }

        if (distance <= Vid.Weapon.BattleRange)
        {
            if (!aimed)
                return AttackResult.Busy;

            int count = Vid.ChildCounts[(int)SpriteAnimation.Fight];

            if (Map.Action(this, ActionCode.GetAmmo) < Math.Abs(count))
                return AttackResult.NoAmmo;

            Map.Action(this, ActionCode.AddAmmo, -count);

            ChangeAnimation(SpriteAnimation.Fight);
            Timer = Vid.Weapon.ReloadTime + ReloadTimerOffset;

            return AttackResult.Attacked;
        }

        if (Command == Command.Attack)
            return AttackResult.Approach;

        return distance < Vid.Weapon.DetectRange * 2 ? AttackResult.Pursue : AttackResult.GiveUp;
    }

    internal virtual int Action(ActionCode act, int var1, int var2, int var3) => SpriteAction(act, var1, var2, var3);

    internal void CreateChildAs(SpriteAnimation state)
    {
        var previous = Animation;
        SetAnimationValue(state);
        CreateChild();
        SetAnimationValue(previous);
    }

    internal void SetAnimationValue(SpriteAnimation state) => Animation = state;

    [EngineRoutine(0x00446480)]
    internal ReadOnlyMemory<byte> GetTextItems()
    {
        if (Items.Count == 0)
            return default;

        var text = new List<byte>();
        foreach (int item in Items)
        {
            text.AddRange(ItemMark);
            AppendNumber(text, item);
        }

        text.AddRange(Map.ItemsEnd);

        return text.ToArray();
    }

    [EngineRoutine(0x00446570)]
    internal void SetTextItems(ReadOnlyMemory<byte> text)
    {
        if (text.IsEmpty)
            return;

        EnsureExtraData();
        text = text.After(ItemMark);
        int item = 0;
        while (!text.IsEmpty)
        {
            if (CRuntime.TryScanInteger(text.Span, out int scanned))
            {
                item = scanned;
            }

            ExtraData!.Items.Insert(item);
            text = text.After(ItemMark);
        }
    }

    private protected static void KeepHoverHeight(Sprite sprite, Vid vid, float hover, ref Vector3 next)
    {
        if (sprite.Position.Z.IsExactly(next.Z) || vid.Properties.HasFlag(ObjectProperties.Gravity) || hover.IsExactlyZero())
            return;

        float height = hover;

        if (hover <= sprite.Position.Z)
        {
            if (sprite.Position.Z <= hover)
            {
                height = next.Z;

                if (!vid.Properties.HasFlag(ObjectProperties.SelfMoving))
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
    private protected void BreakLink()
    {
        Link?.Owner = Owner;

        if (Owner is not null)
        {
            Owner.Link = Link;
            Owner = null;
        }

        Link = null;
    }

    private static void AppendNumber(List<byte> text, int value)
    {
        Span<byte> number = stackalloc byte[11];
        Utf8Formatter.TryFormat(value, number, out int length);
        text.AddRange(number[..length]);
    }

    private Angle GetShownDirection(Angle direction)
    {
        if (VerticalSpeed.IsExactlyZero() || !Vid.Properties.HasFlag(ObjectProperties.VerticalDirection))
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

    private void SetFlag(SpriteFlags flag, bool isSet) => _flags = (isSet ? flag : SpriteFlags.None) | _flags & ~flag;
}

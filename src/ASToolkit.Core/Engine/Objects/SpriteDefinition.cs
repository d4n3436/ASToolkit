using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents a sprite definition with its records, frame layout, script handlers and per-army counts.
/// </summary>
public sealed class SpriteDefinition
{
    /// <summary>
    /// Represents the value the game files use for an unlimited speed, lifetime or amount.
    /// </summary>
    public const int Unlimited = 999999;
    
    /// <summary>
    /// Represents the scale from rates per second to rates per millisecond.
    /// </summary>
    public const float PerMillisecond = 0.001f;
    
    /// <summary>
    /// Represents the scale from accelerations per second squared to per millisecond squared.
    /// </summary>
    public const float PerMillisecondSquared = 1e-6f;
    
    /// <summary>
    /// Represents the number of armies.
    /// </summary>
    public const int ArmyCount = 4;
    
    /// <summary>
    /// Represents a script handler slot with no function bound.
    /// </summary>
    public const int NoHandler = -1;
    
    /// <summary>
    /// Represents the number of sprite states.
    /// </summary>
    public const int StateCount = (int)SpriteAnimation.Death2 + 1;

    private readonly GameObject _object;
    private readonly WeaponRecord _weapon;
    private readonly int[] _frameDurations;
    private readonly int[] _children = new int[StateCount];
    private readonly int[] _childCounts = new int[StateCount];
    private readonly int[] _counts = new int[ArmyCount];
    private readonly int[] _killed = new int[ArmyCount];
    private readonly int[] _recolored = new int[ArmyCount];
    private readonly int[] _armyMaxHp = new int[ArmyCount];
    private readonly int[] _armyMaxUnits = new int[ArmyCount];

    /// <summary>
    /// Creates the definition of an object record.
    /// </summary>
    /// <param name="id">The object id.</param>
    /// <param name="obj">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="spriteSpeed">The frame duration of the definition's sprite.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    /// <param name="layer">The update list of the definition.</param>
    public SpriteDefinition(KnownObject id, in GameObject obj, in WeaponRecord weapon, int spriteSpeed, int totalFrames = 0, int layer = 0)
    {
        Id = id;
        RecordId = id;
        Identity = this;
        _object = obj;
        _weapon = weapon;
        Layout = new FrameLayout(obj, spriteSpeed, totalFrames);
        ClearHandlers();
        _frameDurations = new int[(int)SpriteAnimation.Death2 + 1];

        SpriteSpeed = spriteSpeed;
        Layer = layer;
        ZSpeed = ScaleRate(obj.MaxZSpeed, PerMillisecond);
        Acceleration = ScaleRate(obj.Acceleration, PerMillisecondSquared);
        Deceleration = ScaleRate(obj.Deceleration, PerMillisecondSquared);
        WeaponFlags = weapon.Flags;
        Behavior = weapon.Behavior;
        Army = weapon.Army;
        Rank = weapon.Rank;
        Targets = weapon.Targets;
        Half = obj.Size.AsVector2() * 0.5f;
        BattleRange = weapon.BattleRange;
        MinRange = weapon.MinRange;
        ReloadTime = weapon.ReloadTime;
        AttackTime = Math.Max(weapon.ReloadTime, GetAnimationTime(obj, SpriteAnimation.Fight, spriteSpeed));
        LoadRecord();
        ResetLevelState();
    }

    /// <summary>
    /// Reloads the values the level script can change from the records.
    /// </summary>
    [EngineRoutine(0x00413c30)]
    public void LoadRecord()
    {
        MaxHp = _object.MaxHp;
        Speed = ScaleRate(_object.MaxSpeed, PerMillisecond);
        MoveMask = _object.MoveMask;
        Damage = _object.DeathDamage;
        Lifetime = _object.Lifetime;
        DetectRange = _weapon.DetectRange;
        Aim = _weapon.Aim;
        AmmoCapacity = _weapon.Ammo;
        BuildTime = _weapon.BuildTime;
        NotCreatedAsChild = 0;
        HasKeyframes = GetHasKeyframes(_weapon);
        Layout.LoadFrameDurations(_object, SpriteSpeed);
        LoadAnimations(SpriteSpeed);
        for (int state = 0; state < StateCount; state++)
        {
            _children[state] = _object.Children[state];
            _childCounts[state] = _object.ChildCounts[state];
        }
    }

    /// <summary>
    /// Sets the frame duration of every state.
    /// </summary>
    /// <param name="duration">The frame duration in milliseconds.</param>
    public void SetFrameSpeed(int duration)
    {
        SpriteSpeed = duration;
        Layout.SetFrameDurations(duration);
        Array.Fill(_frameDurations, Math.Max(1, duration));
    }

    private void LoadAnimations(int spriteSpeed)
    {
        for (int i = 0; i < _frameDurations.Length; i++)
        {
            _frameDurations[i] = Math.Max(1, _object.GetFrameDuration((SpriteAnimation)i, spriteSpeed));
        }
    }

    /// <summary>
    /// Gets the script function bound to each state, or NoHandler.
    /// </summary>
    public int[] StateHandlers { get; } = new int[StateCount];

    /// <summary>
    /// Gets or sets the script function run when a sprite of the definition is destroyed.
    /// </summary>
    public int DestroyHandler { get; set; } = NoHandler;

    /// <summary>
    /// Gets or sets the script function run when a sprite of the definition is damaged.
    /// </summary>
    public int DamageHandler { get; set; } = NoHandler;

    /// <summary>
    /// Gets or sets the script function run when a sprite of the definition collides.
    /// </summary>
    public int CollisionHandler { get; set; } = NoHandler;

    /// <summary>
    /// Unbinds every script handler of the definition.
    /// </summary>
    public void ClearHandlers()
    {
        Array.Fill(StateHandlers, NoHandler);
        DestroyHandler = NoHandler;
        DamageHandler = NoHandler;
        CollisionHandler = NoHandler;
    }

    /// <summary>
    /// Clears the per-army counts and limits a level starts with.
    /// </summary>
    [EngineRoutine(0x00413980)]
    public void ResetLevelState()
    {
        Array.Clear(_counts);
        Array.Clear(_killed);
        Array.Clear(_recolored);
        Array.Fill(_armyMaxHp, MaxHp);
        Array.Fill(_armyMaxUnits, -1);
        MaxUnits = -1;
    }

    /// <summary>
    /// Gets or sets the id sprites of the definition report.
    /// </summary>
    public KnownObject Id { get; set; }

    /// <summary>
    /// Gets the id of the record the definition was loaded from.
    /// </summary>
    public KnownObject RecordId { get; }

    /// <summary>
    /// Gets or sets the definition that sprites of this definition are created as.
    /// </summary>
    public SpriteDefinition Identity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether sprites of the definition are hidden.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Gets the speed as the level script reads it, in pixels per second.
    /// </summary>
    public int ScriptSpeed => Speed.IsNear(Unlimited) ? Unlimited : (int)(Speed * 1000);

    /// <summary>
    /// Gets the object record of the definition.
    /// </summary>
    public ref readonly GameObject Object => ref _object;

    /// <summary>
    /// Gets the weapon record of the definition.
    /// </summary>
    public ref readonly WeaponRecord Weapon => ref _weapon;

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates color, scale or offset.
    /// </summary>
    public bool HasKeyframes { get; set; }

    internal FrameLayout Layout { get; }

    /// <summary>
    /// Gets the frame duration of the definition's sprite.
    /// </summary>
    public int SpriteSpeed { get; private set; }

    /// <summary>
    /// Gets the weapon flags of the definition.
    /// </summary>
    public WeaponFlags WeaponFlags { get; }

    /// <summary>
    /// Gets the behavior bits of the definition.
    /// </summary>
    public WeaponBehavior Behavior { get; }

    /// <summary>
    /// Gets the army of the definition.
    /// </summary>
    public Army Army { get; }

    /// <summary>
    /// Gets the rank of the definition.
    /// </summary>
    public int Rank { get; }

    /// <summary>
    /// Gets the sprite types the definition attacks.
    /// </summary>
    public SpriteType Targets { get; }

    /// <summary>
    /// Gets a value indicating whether other units push sprites of the definition aside.
    /// </summary>
    public bool CanBePushed => WeaponFlags.HasFlag(WeaponFlags.Pushable);

    /// <summary>
    /// Gets a value indicating whether the definition attacks before it faces its target.
    /// </summary>
    public bool AttacksWhileTurning => WeaponFlags.HasFlag(WeaponFlags.AttacksWhileTurning);

    /// <summary>
    /// Gets a value indicating whether the definition attacks only targets ahead of it.
    /// </summary>
    public bool NeedsForwardTarget => WeaponFlags.HasFlag(WeaponFlags.ForwardTargets);

    /// <summary>
    /// Gets a value indicating whether the definition alternates the two children of a mirrored pair.
    /// </summary>
    public bool AttacksTwice => WeaponFlags.HasFlag(WeaponFlags.DoubleAttack);

    /// <summary>
    /// Gets the state a sprite of the definition starts in.
    /// </summary>
    public SpriteAnimation InitialAnimation =>
        Layout.GetFrameCount(SpriteAnimation.Stand) == 0 && Layout.GetFrameCount(SpriteAnimation.Go) == 0 &&
        Layout.GetFrameCount(SpriteAnimation.Death) != 0 ? SpriteAnimation.Death
        : Layout.GetFrameCount(SpriteAnimation.Birth) != 0 ? SpriteAnimation.Birth : SpriteAnimation.Stand;

    /// <summary>
    /// Returns the frame duration of a state.
    /// </summary>
    /// <param name="animation">The state.</param>
    /// <returns>The duration in milliseconds.</returns>
    public int GetVisualFrameDuration(SpriteAnimation animation) => _frameDurations[(int)animation];

    /// <summary>
    /// Returns the length of a state's animation.
    /// </summary>
    /// <param name="animation">The state.</param>
    /// <returns>The length in milliseconds.</returns>
    public int GetAnimationDuration(SpriteAnimation animation) => Math.Max(1, Object.FrameCounts[(int)animation]) * GetVisualFrameDuration(animation);

    /// <summary>
    /// Returns the direction of the direction frame a sprite of this definition shows for a direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The frame's direction, or the direction itself when the definition has no direction frames.</returns>
    [EngineRoutine(0x00442e00)]
    public Angle GetFrameDirection(Angle direction) => Layout.Directions != 0 ? direction.GetFrameDirection(Layout.Directions) : direction;

    /// <summary>
    /// Returns where a link sits relative to a sprite of this definition facing a direction.
    /// </summary>
    /// <param name="link">The link's definition.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <returns>The offset from the sprite's position.</returns>
    public Vector3 GetLinkOffset(SpriteDefinition link, Angle direction) => Object.GetLinkOffset(link.Object, null, 0, direction);

    /// <summary>
    /// Gets or sets the health of a new sprite of the definition.
    /// </summary>
    public int MaxHp { get; set; }

    /// <summary>
    /// Gets or sets the top speed of the definition in pixels per millisecond.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Gets the update list of the definition.
    /// </summary>
    public int Layer { get; }

    /// <summary>
    /// Gets the vertical speed of the definition in pixels per millisecond.
    /// </summary>
    public float ZSpeed { get; }

    /// <summary>
    /// Gets the acceleration of the definition.
    /// </summary>
    public float Acceleration { get; }

    /// <summary>
    /// Gets the deceleration of the definition.
    /// </summary>
    public float Deceleration { get; }

    /// <summary>
    /// Gets half the footprint of the definition.
    /// </summary>
    public Vector2 Half { get; }

    internal CollisionVolume GetCollisionVolume(Vector3 position) => new(position, Half, Object.Size.Z, MoveMask);

    /// <summary>
    /// Gets or sets the collision layers the definition moves through.
    /// </summary>
    public CollisionLayers MoveMask { get; set; }

    /// <summary>
    /// Gets or sets the damage a sprite of the definition deals when it dies.
    /// </summary>
    public int Damage { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of a sprite of the definition in milliseconds.
    /// </summary>
    public int Lifetime { get; set; }

    /// <summary>
    /// Gets or sets the flag that keeps the definition from being created as a child.
    /// </summary>
    public int NotCreatedAsChild { get; set; }

    /// <summary>
    /// Gets the child object of each state.
    /// </summary>
    public Span<int> Children => _children;

    /// <summary>
    /// Gets the child count of each state.
    /// </summary>
    public Span<int> ChildCounts => _childCounts;

    /// <summary>
    /// Gets the living sprites of the definition per army.
    /// </summary>
    public Span<int> Counts => _counts;

    /// <summary>
    /// Gets the killed sprites of the definition per army.
    /// </summary>
    public Span<int> Killed => _killed;

    /// <summary>
    /// Gets the recolored sprites of the definition per army.
    /// </summary>
    public Span<int> Recolored => _recolored;

    /// <summary>
    /// Gets the health of a new sprite of the definition per army.
    /// </summary>
    public Span<int> ArmyMaxHp => _armyMaxHp;

    /// <summary>
    /// Gets the sprite limit of the definition per army, negative when unlimited.
    /// </summary>
    public Span<int> ArmyMaxUnits => _armyMaxUnits;

    /// <summary>
    /// Gets or sets the sprite limit of the definition, negative when unlimited.
    /// </summary>
    public int MaxUnits { get; set; }

    /// <summary>
    /// Gets the radius of the definition's death blast.
    /// </summary>
    public float BlastRadius => Object.DeathRange;

    /// <summary>
    /// Gets a value indicating whether the definition's death blast damages everything in its radius.
    /// </summary>
    public bool IsExplosive => Object.Properties.HasFlag(ObjectProperties.RadialDamage);

    /// <summary>
    /// Gets a value indicating whether the definition's damage spares its own army.
    /// </summary>
    public bool SkipsOwnArmy => Object.Properties.HasFlag(ObjectProperties.NoFriendlyDamage);
    
    /// <summary>
    /// Gets or sets the range at which the definition sees enemies.
    /// </summary>
    public float DetectRange { get; set; }

    /// <summary>
    /// Gets the range at which the definition attacks.
    /// </summary>
    public float BattleRange { get; }

    /// <summary>
    /// Gets the range under which the definition does not attack.
    /// </summary>
    public float MinRange { get; }

    /// <summary>
    /// Gets or sets the aim scatter of the definition.
    /// </summary>
    public float Aim { get; set; }

    /// <summary>
    /// Gets or sets the ammunition the definition holds when full.
    /// </summary>
    public int AmmoCapacity { get; set; }

    /// <summary>
    /// Gets or sets the time a building takes to build the definition.
    /// </summary>
    public int BuildTime { get; set; }

    /// <summary>
    /// Gets the time between the definition's attacks.
    /// </summary>
    public int ReloadTime { get; }

    /// <summary>
    /// Gets the time an attack of the definition takes.
    /// </summary>
    public int AttackTime { get; }
    
    /// <summary>
    /// Gets the object the definition launches when it attacks.
    /// </summary>
    public KnownObject FightChild => (KnownObject)_children[(int)SpriteAnimation.Fight];

    /// <summary>
    /// Gets the number of objects the definition launches per attack.
    /// </summary>
    public int FightCount => Math.Max(1, _childCounts[(int)SpriteAnimation.Fight]);

    /// <summary>
    /// Gets the object the definition leaves when it dies.
    /// </summary>
    public KnownObject DeathChild => (KnownObject)_children[(int)SpriteAnimation.Death];

    /// <summary>
    /// Gets a value indicating whether the definition attacks.
    /// </summary>
    public bool IsArmed => BattleRange > 0;

    /// <summary>
    /// Gets or sets the definition linked to this definition.
    /// </summary>
    public SpriteDefinition? LinkDefinition { get; set; }

    private static int GetAnimationTime(in GameObject obj, SpriteAnimation animation, int spriteSpeed) =>
        obj.FrameCounts[(int)animation] * obj.GetFrameDuration(animation, spriteSpeed);

    [EngineRoutine(0x00413a60)]
    private static bool GetHasKeyframes(in WeaponRecord weapon)
    {
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            if (weapon.RedKeys[key] != 0 || weapon.GreenKeys[key] != 0 || weapon.BlueKeys[key] != 0 || weapon.AlphaKeys[key] != 0 ||
                !weapon.ScaleXKeys[key].IsNear(1) || !weapon.ScaleYKeys[key].IsNear(1) || !weapon.ScaleHeightKeys[key].IsNear(1) ||
                !weapon.OffsetXKeys[key].IsZero() || !weapon.OffsetYKeys[key].IsZero())
                return true;
        }

        return false;
    }

    private static float ScaleRate(float rate, float scale) => rate.IsNear(Unlimited) ? rate : rate * scale;
}

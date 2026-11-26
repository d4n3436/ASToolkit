using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the data and frames shared by every sprite of one object ID.
/// </summary>
public class Vid
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
    /// Represents a script handler slot that doesn't have a function bound.
    /// </summary>
    public const int NoHandler = -1;

    /// <summary>
    /// Represents the mask that reduces an army to its slot.
    /// </summary>
    public const int ArmyMask = ArmyCount - 1;

    /// <summary>
    /// Represents the number of sprite states.
    /// </summary>
    public const int StateCount = (int)SpriteAnimation.Death2 + 1;

    /// <summary>
    /// Represents the army value that sets the gamma of every army.
    /// </summary>
    public const int AllArmies = 4;

    private protected readonly Map _map;

    private const float BallisticRise = 1.1f;
    private const float BallisticDrop = 0.9f;
    private const int CoefficientScale = 16;
    private const int HpScale = 256;

    private static readonly Vector3 DefaultSize = new(24, 16, 20);

    private readonly int[] _frameCounts = new int[StateCount];
    private readonly int[] _sounds = new int[StateCount];
    private readonly int[] _durations = new int[StateCount];
    private readonly float[] _childX = new float[StateCount];
    private readonly float[] _childY = new float[StateCount];
    private readonly float[] _childZ = new float[StateCount];
    private readonly int[] _children = new int[StateCount];
    private readonly Vid?[] _childVids = new Vid?[StateCount];
    private readonly int[] _childCounts = new int[StateCount];
    private readonly int[] _firstFrames = new int[StateCount];
    private readonly int[] _framesPerDirection = new int[StateCount];
    private readonly int[] _armyMaxUnits = new int[ArmyCount];
    private readonly int[] _counts = new int[ArmyCount];
    private readonly int[] _killed = new int[ArmyCount];
    private readonly int[] _recolored = new int[ArmyCount];
    private readonly int[] _armyMaxHp = new int[ArmyCount];
    private readonly Gamma[] _gammas = new Gamma[ArmyCount];
    private AlienShooterWeaponRecord[] _weapons;
    private ReadOnlyMemory<int> _heightFrameStarts;
    private VidFlags _flags;

    /// <summary>
    /// Initializes a new instance of the <see cref="Vid"/> class from an object record and loads its parameters.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="spriteSpeed">The frame duration of the sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    /// <param name="layer">The update list of sprites of the object ID.</param>
    /// <exception cref="InvalidDataException">The record doesn't have any directions.</exception>
    public Vid(Map map, KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, int spriteSpeed, int totalFrames = 0, ObjectLayer layer = ObjectLayer.Ground)
        : this(map, id, gameObject, weapons, spriteSpeed, totalFrames, layer, null)
    {
        LoadParameters(gameObject);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Vid"/> class from an object record and the header of its sprite file, without loading its parameters.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    protected Vid(Map map, KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, in VidResource file, int totalFrames)
        : this(map, id, gameObject, weapons, file.Speed, totalFrames, ObjectLayer.Ground, file)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Vid"/> class from an object record without loading its parameters.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapons">The weapon table.</param>
    /// <param name="spriteSpeed">The frame duration of the sprite file.</param>
    /// <param name="totalFrames">The frame count of the sprite file.</param>
    /// <param name="layer">The update list of sprites of the object ID.</param>
    /// <param name="file">The sprite file, or <see langword="null"/> without one.</param>
    [EngineRoutine(0x00413710)]
    protected Vid(Map map, KnownObject id, in GameObject gameObject, AlienShooterWeaponRecord[] weapons, int spriteSpeed, int totalFrames, ObjectLayer layer, VidResource? file)
    {
        _map = map;
        Id = id;
        RecordId = id;
        Identity = this;
        NextMirror = this;
        _weapons = weapons;
        TotalFrames = totalFrames;
        CopyRecord(gameObject);

        SpriteSpeed = spriteSpeed;
        Layer = layer;
        if (file is not null)
        {
            FileType = file.Value.Type;
            FrameSize = file.Value.Size;
            FileFrameCount = file.Value.FrameCount;
        }

        ResetSprites();
    }

    private protected Vid(Vid source)
        : this(source._map, KnownObject.None, new GameObject(SpriteClass.Primitive, DefaultSize), source._weapons, Map.DefaultSpriteSpeed, source.TotalFrames, ObjectLayer.Menu, null)
    {
        NextMirror = source.NextMirror;
        source.NextMirror = this;
        Layer = source.Layer;
        FileType = source.FileType;
        FileFrameCount = source.FileFrameCount;
        SpriteSpeed = source.SpriteSpeed;
        FrameSize = source.FrameSize;
    }

    /// <summary>
    /// Gets or sets the object ID that sprites of this object ID report.
    /// </summary>
    public KnownObject Id { get; set; }

    /// <summary>
    /// Gets the name of the object this data was loaded from.
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the sprite type bits of this object ID.
    /// </summary>
    public OldSpriteType SpriteType { get; private set; }

    /// <summary>
    /// Gets the sprite class of sprites of this object ID.
    /// </summary>
    public SpriteClass Class { get; private set; }

    /// <summary>
    /// Gets the properties of this object ID.
    /// </summary>
    public ObjectProperties Properties { get; private set; }

    /// <summary>
    /// Gets or sets the collision layers sprites of this object ID move through.
    /// </summary>
    public CollisionLayers MoveMask { get; set; }

    /// <summary>
    /// Gets the size of a sprite of this object ID.
    /// </summary>
    public Vector3 Size { get; private set; }

    /// <summary>
    /// Gets or sets the health of a new sprite of this object ID.
    /// </summary>
    public int MaxHp { get; set; }

    /// <summary>
    /// Gets or sets the top speed of sprites of this object ID in pixels per millisecond.
    /// </summary>
    public float Speed { get; set; }

    /// <summary>
    /// Gets the vertical speed of sprites of this object ID in pixels per millisecond.
    /// </summary>
    public float ZSpeed { get; private set; }

    /// <summary>
    /// Gets the acceleration of sprites of this object ID.
    /// </summary>
    public float Acceleration { get; private set; }

    /// <summary>
    /// Gets the deceleration of sprites of this object ID.
    /// </summary>
    public float Deceleration { get; private set; }

    /// <summary>
    /// Gets the steps of a full turn that sprites of this object ID make per millisecond; 0 turns never and 999999 turns at once.
    /// </summary>
    public float RotationRate { get; private set; }

    /// <summary>
    /// Gets the index of the weapon record of this object ID, or 0 if it doesn't have one.
    /// </summary>
    public int WeaponIndex { get; private set; }

    /// <summary>
    /// Gets the radius of the damage a sprite of this object ID deals when it is destroyed.
    /// </summary>
    public float DeathRange { get; private set; }

    /// <summary>
    /// Gets or sets the damage a sprite of this object ID deals when it dies.
    /// </summary>
    public int Damage { get; set; }

    /// <summary>
    /// Gets the offset of the link from a sprite of this object ID.
    /// </summary>
    public Vector3 LinkOffset { get; private set; }

    /// <summary>
    /// Gets the object ID of the link, or <see cref="KnownObject.None"/> if there isn't one.
    /// </summary>
    public KnownObject Link { get; private set; }

    /// <summary>
    /// Gets or sets the shared data of the object ID linked to this one.
    /// </summary>
    public Vid? LinkVid { get; set; }

    /// <summary>
    /// Gets the height of the top of a sprite of this object ID.
    /// </summary>
    public float TopZ { get; private set; }

    /// <summary>
    /// Gets the highest step a sprite of this object ID climbs.
    /// </summary>
    public float StepHeight { get; private set; }

    /// <summary>
    /// Gets the highest drop a sprite of this object ID steps down.
    /// </summary>
    public float DropHeight { get; private set; }

    /// <summary>
    /// Gets or sets the lifetime of a sprite of this object ID in milliseconds.
    /// </summary>
    public int Lifetime { get; set; }

    /// <summary>
    /// Gets the frame count of each state.
    /// </summary>
    public ReadOnlySpan<int> FrameCounts => _frameCounts;

    /// <summary>
    /// Gets the sound ID of each animation state.
    /// </summary>
    public ReadOnlySpan<int> Sounds => _sounds;

    /// <summary>
    /// Gets the horizontal offset of the child of each state.
    /// </summary>
    public ReadOnlySpan<float> ChildX => _childX;

    /// <summary>
    /// Gets the vertical offset of the child of each state.
    /// </summary>
    public ReadOnlySpan<float> ChildY => _childY;

    /// <summary>
    /// Gets the height offset of the child of each state.
    /// </summary>
    public ReadOnlySpan<float> ChildZ => _childZ;

    /// <summary>
    /// Gets the child object of each state.
    /// </summary>
    public Span<int> Children => _children;

    /// <summary>
    /// Gets the shared data of the child object of each state, or <see langword="null"/> where the state has none.
    /// </summary>
    public Span<Vid?> ChildVids => _childVids;

    /// <summary>
    /// Gets the child count of each state.
    /// </summary>
    public Span<int> ChildCounts => _childCounts;

    /// <summary>
    /// Gets the gamma applied to sprites of this object ID.
    /// </summary>
    public Gamma ObjectGamma { get; private set; }

    /// <summary>
    /// Gets the factors that scale sprites of this object ID.
    /// </summary>
    public Vector3 Scale { get; private set; }

    /// <summary>
    /// Gets the path of the sprite file the frames were loaded from.
    /// </summary>
    public string Path { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets or sets the flags of the sprite file.
    /// </summary>
    public VidType FileType { get; set; }

    /// <summary>
    /// Gets the frame duration of the sprite file.
    /// </summary>
    public int SpriteSpeed { get; private set; }

    /// <summary>
    /// Gets the number of frames in the sprite file, or 0 if there isn't a sprite file.
    /// </summary>
    public short FileFrameCount { get; protected set; }

    /// <summary>
    /// Gets the width and height of a frame of the sprite file, in pixels.
    /// </summary>
    public Size FrameSize { get; protected set; }

    /// <summary>
    /// Gets half the footprint of sprites of this object ID.
    /// </summary>
    public Vector2 Half { get; private set; }

    /// <summary>
    /// Gets the update list of sprites of this object ID.
    /// </summary>
    public ObjectLayer Layer { get; protected set; }

    /// <summary>
    /// Gets or sets the sprite limit of this object ID, negative when unlimited.
    /// </summary>
    public int MaxUnits { get; set; }

    /// <summary>
    /// Gets the sprite limit of this object ID per army, negative when unlimited.
    /// </summary>
    public Span<int> ArmyMaxUnits => _armyMaxUnits;

    /// <summary>
    /// Gets the living sprites of this object ID per army.
    /// </summary>
    public Span<int> Counts => _counts;

    /// <summary>
    /// Gets the killed sprites of this object ID per army.
    /// </summary>
    public Span<int> Killed => _killed;

    /// <summary>
    /// Gets the health of a new sprite of this object ID per army.
    /// </summary>
    public Span<int> ArmyMaxHp => _armyMaxHp;

    /// <summary>
    /// Gets the gamma sprites of this object ID are drawn with per army.
    /// </summary>
    public Span<Gamma> Gammas => _gammas;

    /// <summary>
    /// Gets the script function bound to each state, or NoHandler.
    /// </summary>
    public int[] StateHandlers { get; } = new int[StateCount];

    /// <summary>
    /// Gets or sets the script function run when a sprite of this object ID is destroyed.
    /// </summary>
    public int DestroyHandler { get; set; } = NoHandler;

    /// <summary>
    /// Gets or sets the script function run when a sprite of this object ID is damaged.
    /// </summary>
    public int DamageHandler { get; set; } = NoHandler;

    /// <summary>
    /// Gets or sets the script function run when a sprite of this object ID collides.
    /// </summary>
    public int CollisionHandler { get; set; } = NoHandler;

    /// <summary>
    /// Gets or sets the real time at which a sprite of this object ID was last created or recounted.
    /// </summary>
    public uint LastCreatedAt { get; set; }

    /// <summary>
    /// Gets the weapon record of this object ID.
    /// </summary>
    public ref AlienShooterWeaponRecord Weapon => ref _weapons[WeaponIndex < _weapons.Length ? WeaponIndex : 0];

    /// <summary>
    /// Gets or sets the next object ID in the ring of object IDs that share this one's frames.
    /// </summary>
    public Vid NextMirror { get; set; }

    /// <summary>
    /// Gets or sets the shared data that sprites of this object ID are created with.
    /// </summary>
    public Vid Identity { get; set; }

    /// <summary>
    /// Gets the height points of this object ID.
    /// </summary>
    public List<Vector3> HeightPoints { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether sprites of this object ID carry a lifetime, a keyframe track, a stream period and a top speed of their own.
    /// </summary>
    public bool HasTimers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the color.
    /// </summary>
    public bool HasColorKeyframes
    {
        get => _flags.HasFlag(VidFlags.ColorKeyframes);
        set => SetFlag(VidFlags.ColorKeyframes, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the scale.
    /// </summary>
    public bool HasScaleKeyframes
    {
        get => _flags.HasFlag(VidFlags.ScaleKeyframes);
        set => SetFlag(VidFlags.ScaleKeyframes, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the offset.
    /// </summary>
    public bool HasOffsetKeyframes
    {
        get => _flags.HasFlag(VidFlags.OffsetKeyframes);
        set => SetFlag(VidFlags.OffsetKeyframes, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the rotation.
    /// </summary>
    public bool HasRotationKeyframes
    {
        get => _flags.HasFlag(VidFlags.RotationKeyframes);
        set => SetFlag(VidFlags.RotationKeyframes, value);
    }

    /// <summary>
    /// Gets a value indicating whether the weapon record animates color, scale, offset or rotation.
    /// </summary>
    public bool HasKeyframes => (_flags & VidFlags.Keyframes) != 0;

    /// <summary>
    /// Gets or sets a value indicating whether a region can replace the object ID of sprites created from it.
    /// </summary>
    public bool IsRegionReplaced
    {
        get => _flags.HasFlag(VidFlags.RegionReplaced);
        set => SetFlag(VidFlags.RegionReplaced, value);
    }

    /// <summary>
    /// Gets a value indicating whether the record of this object ID has the built terrain class.
    /// </summary>
    public bool IsBuiltTerrainClass
    {
        get => _flags.HasFlag(VidFlags.BuiltTerrain);
        private set => SetFlag(VidFlags.BuiltTerrain, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether sprites of this object ID are hidden.
    /// </summary>
    public bool IsHidden
    {
        [EngineRoutine(0x0043a1b0)]
        get => _flags.HasFlag(VidFlags.Hidden);
        set => SetFlag(VidFlags.Hidden, value);
    }

    /// <summary>
    /// Gets or sets the flag that keeps sprites of this object ID from being created as children.
    /// </summary>
    public int NotCreatedAsChild
    {
        [EngineRoutine(0x0043a300)]
        get;
        [EngineRoutine(0x0043a310)]
        set;
    }

    /// <summary>
    /// Gets the ID of the record this data was loaded from.
    /// </summary>
    public KnownObject RecordId { get; internal set; }

    /// <summary>
    /// Gets the speed in pixels per second.
    /// </summary>
    public int ScriptSpeed => Speed.IsExactly(Unlimited) ? Unlimited : (int)(Speed * 1000);

    /// <summary>
    /// Gets a value indicating whether other sprites push sprites of this object ID aside.
    /// </summary>
    public bool CanBePushed => Weapon.Flags.HasFlag(WeaponFlags.Pushable);

    /// <summary>
    /// Gets a value indicating whether sprites of this object ID attack before they face their target.
    /// </summary>
    public bool CanAttackWhileTurning => Weapon.Flags.HasFlag(WeaponFlags.AttacksWhileTurning);

    /// <summary>
    /// Gets a value indicating whether sprites of this object ID attack only targets ahead of them.
    /// </summary>
    public bool HasForwardTargets => Weapon.Flags.HasFlag(WeaponFlags.ForwardTargets);

    /// <summary>
    /// Gets a value indicating whether sprites of this object ID alternate the two children of a mirrored pair.
    /// </summary>
    public bool HasDoubleAttack => Weapon.Flags.HasFlag(WeaponFlags.DoubleAttack);

    /// <summary>
    /// Gets the state a sprite of this object ID starts in.
    /// </summary>
    public SpriteAnimation InitialAnimation =>
        GetFrameCount(SpriteAnimation.Stand) == 0 && GetFrameCount(SpriteAnimation.Go) == 0 &&
        GetFrameCount(SpriteAnimation.Death) != 0 ? SpriteAnimation.Death
        : GetFrameCount(SpriteAnimation.Birth) != 0 ? SpriteAnimation.Birth : SpriteAnimation.Stand;

    /// <summary>
    /// Gets a value indicating whether the death blast of sprites of this object ID damages everything in its radius.
    /// </summary>
    public bool IsExplosive => Properties.HasFlag(ObjectProperties.RadialDamage);

    /// <summary>
    /// Gets a value indicating whether the damage of sprites of this object ID spares their own army.
    /// </summary>
    public bool ShouldSkipOwnArmy => Properties.HasFlag(ObjectProperties.NoFriendlyDamage);

    /// <summary>
    /// Gets the time an attack of sprites of this object ID takes.
    /// </summary>
    public int AttackTime => Math.Max(Weapon.ReloadTime, GetAnimationTime(SpriteAnimation.Fight));

    /// <summary>
    /// Gets the object sprites of this object ID launch when they attack.
    /// </summary>
    public KnownObject FightChild => (KnownObject)_children[(int)SpriteAnimation.Fight];

    /// <summary>
    /// Gets the number of objects sprites of this object ID launch per attack.
    /// </summary>
    public int FightCount => Math.Max(1, _childCounts[(int)SpriteAnimation.Fight]);

    /// <summary>
    /// Gets the object a sprite of this object ID leaves when it dies.
    /// </summary>
    public KnownObject DeathChild => (KnownObject)_children[(int)SpriteAnimation.Death];

    /// <summary>
    /// Gets a value indicating whether sprites of this object ID attack.
    /// </summary>
    public bool IsArmed => Weapon.BattleRange > 0;

    /// <summary>
    /// Gets a value indicating whether sprites of this object ID are born as smoke.
    /// </summary>
    public bool IsBirthAsSmoke
    {
        [EngineRoutine(0x0043a320)]
        get => Properties.HasFlag(ObjectProperties.BirthAsSmoke);
    }

    internal int Directions { get; private set; }

    internal int TotalFrames { get; }

    internal int DirectionOffset { get; private set; }

    internal bool CanMove { get; private set; }

    /// <summary>
    /// Sets where the height points of each frame of the sprite file start in <see cref="HeightPoints"/>.
    /// </summary>
    /// <param name="starts">The index of the first height point of each frame.</param>
    public void SetHeightFrameStarts(ReadOnlyMemory<int> starts) => _heightFrameStarts = starts;

    /// <summary>
    /// Draws a sprite with the frames of this object ID.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="sprite">The sprite that gives the position, frame and gamma; it may be of another object ID.</param>
    public virtual void Draw(Screen screen, Sprite sprite)
    {
    }

    /// <summary>
    /// Draws the outline of the frame of a sprite, stored as points in the frame.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="sprite">The sprite that gives the position and frame.</param>
    public virtual void DrawOutline(Screen screen, Sprite sprite)
    {
    }

    /// <summary>
    /// Draws a sprite of this object ID into the textures of a tile of the hardware ground.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <param name="tile">The tile of the hardware ground.</param>
    /// <param name="colorTexture">The color texture of <paramref name="tile"/>.</param>
    /// <param name="depthTexture">The depth texture of <paramref name="tile"/>.</param>
    [EngineRoutine(0x004127d0)]
    public virtual void DrawToVid(Sprite sprite, HardwareTile tile, Texture colorTexture, Texture depthTexture)
    {
    }

    /// <summary>
    /// Draws a built terrain sprite into the textures of this object ID.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public virtual void AddVidToTextures(Sprite sprite)
    {
    }

    /// <summary>
    /// Sets the frame duration of every state.
    /// </summary>
    /// <param name="duration">The frame duration in milliseconds.</param>
    public void SetFrameSpeed(int duration)
    {
        SpriteSpeed = (ushort)duration;
        Array.Fill(_durations, duration);
    }

    /// <summary>
    /// Returns the length of a state's animation.
    /// </summary>
    /// <param name="animation">The state.</param>
    /// <returns>The length in milliseconds.</returns>
    public int GetAnimationDuration(SpriteAnimation animation) => Math.Max(1, _frameCounts[(int)animation]) * GetVisualFrameDuration(animation);

    /// <summary>
    /// Returns the frame duration of a state.
    /// </summary>
    /// <param name="animation">The state.</param>
    /// <returns>The duration in milliseconds.</returns>
    public int GetVisualFrameDuration(SpriteAnimation animation) => _durations[(int)animation];

    /// <summary>
    /// Sets the update list of sprites of this object ID.
    /// </summary>
    [EngineRoutine(0x00413880)]
    public virtual void SetLayer() => Layer = ObjectLayer.Ground;

    /// <summary>
    /// Returns where a link sits relative to a sprite of this object ID facing a direction.
    /// </summary>
    /// <param name="link">The shared data of the link's object ID.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <returns>The offset from the sprite's position.</returns>
    public Vector3 GetLinkOffset(Vid link, Angle direction)
    {
        if (link.Properties.HasFlag(ObjectProperties.FixedOffset))
            return LinkOffset with { Y = -LinkOffset.Y };

        return new Vector3(direction.Cosine * LinkOffset.X + direction.Sine * LinkOffset.Y, direction.ProjectedSine * LinkOffset.X - direction.ProjectedCosine * LinkOffset.Y, LinkOffset.Z);
    }

    /// <summary>
    /// Resets the per-army counts and limits and unbinds every script handler for a new level.
    /// </summary>
    [EngineRoutine(0x00413980)]
    public void ResetSprites()
    {
        Array.Fill(StateHandlers, NoHandler);
        DestroyHandler = NoHandler;
        DamageHandler = NoHandler;
        CollisionHandler = NoHandler;
        Array.Clear(_counts);
        Array.Clear(_killed);
        Array.Clear(_recolored);
        Array.Fill(_armyMaxHp, MaxHp);
        Array.Fill(_armyMaxUnits, -1);
        MaxUnits = -1;
        LastCreatedAt = 0;
        IsRegionReplaced = false;
    }

    /// <summary>
    /// Hides or shows the sprites of this object ID and of every object ID linked to it.
    /// </summary>
    /// <param name="hidden"><see langword="true"/> to hide the sprites.</param>
    [EngineRoutine(0x00414840)]
    public void SetPropHide(bool hidden)
    {
        IsHidden = hidden;
        for (var link = LinkVid; link is not null; link = link.LinkVid)
        {
            link.IsHidden = hidden;
        }
    }

    /// <summary>
    /// Returns the vertical speed that carries a launched sprite of this object ID up or down a rise over a distance, within its vertical speed limit.
    /// </summary>
    /// <param name="rise">The height to climb, negative to drop.</param>
    /// <param name="distance">The horizontal distance to the goal.</param>
    /// <returns>The vertical speed in pixels per millisecond.</returns>
    [EngineRoutine(0x00414a80)]
    public float CalculateZSpeed(float rise, float distance)
    {
        bool hasGravity = Properties.HasFlag(ObjectProperties.Gravity);
        bool hasGravity2 = !hasGravity && Properties.HasFlag(ObjectProperties.Gravity2);
        float speed;

        if (hasGravity || hasGravity2)
        {
            float gravity = hasGravity ? _map.Constant.Gravitation : _map.Constant.Gravitation2;
            speed = rise * Speed / distance + distance * gravity / Speed * 0.5f;
            speed *= speed > 0 ? BallisticRise : BallisticDrop;
        }
        else
        {
            speed = Properties.HasFlag(ObjectProperties.SelfMoving) ? ZSpeed : TopZ.IsExactlyZero() ? rise * Speed / distance : 0;
        }

        if (speed > ZSpeed)
            return ZSpeed;

        return speed < -ZSpeed ? -ZSpeed : speed;
    }

    /// <summary>
    /// Determines whether sprites of this object ID have a fight child and a weapon.
    /// </summary>
    /// <returns><see langword="true"/> if sprites of this object ID are armed with a weapon of their own; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0043a1e0)]
    public bool CanFight() => ChildVids[(int)SpriteAnimation.Fight] is not null && WeaponIndex != 0;

    /// <summary>
    /// Returns the ammunition a sprite of this object ID holds when full.
    /// </summary>
    /// <returns>The ammunition of the linked object ID's weapon if that object ID can fight; otherwise, the ammunition of this object ID's weapon.</returns>
    [EngineRoutine(0x0043a200)]
    public int GetMaxAmmo() => LinkVid is not null && LinkVid.CanFight() ? LinkVid.Weapon.Ammo : Weapon.Ammo;

    /// <summary>
    /// Returns the health of a new sprite of this object ID for an army.
    /// </summary>
    /// <param name="army">The army, masked to 0 to 3.</param>
    /// <returns>The health.</returns>
    [EngineRoutine(0x0043a230)]
    public int GetArmyMaxHp(int army) => _armyMaxHp[army & ArmyMask];

    /// <summary>
    /// Returns the number of living sprites of this object ID in an army.
    /// </summary>
    /// <param name="army">The army, from 0 to 3.</param>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a250)]
    public int GetCount(int army) => _counts[army];

    /// <summary>
    /// Returns the number of living sprites of this object ID in all armies.
    /// </summary>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a260)]
    public int GetTotalCount() => _counts[3] + _counts[2] + _counts[1] + _counts[0];

    /// <summary>
    /// Returns the number of killed sprites of this object ID in an army.
    /// </summary>
    /// <param name="army">The army, masked to 0 to 3.</param>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a280)]
    public int GetKilled(int army) => _killed[army & ArmyMask];

    /// <summary>
    /// Returns the number of killed sprites of this object ID in all armies.
    /// </summary>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a2a0)]
    public int GetTotalKilled() => _killed[3] + _killed[2] + _killed[1] + _killed[0];

    /// <summary>
    /// Returns the number of recolored sprites of this object ID in an army.
    /// </summary>
    /// <param name="army">The army, masked to 0 to 3.</param>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a2c0)]
    public int GetRecolored(int army) => _recolored[army & ArmyMask];

    /// <summary>
    /// Returns the number of recolored sprites of this object ID in all armies.
    /// </summary>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a2e0)]
    public int GetTotalRecolored() => _recolored[3] + _recolored[2] + _recolored[1] + _recolored[0];

    /// <summary>
    /// Returns the direction of the direction frame a sprite of this object ID shows for a direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The frame's direction, or the direction itself if sprites of this object ID don't use direction frames.</returns>
    [EngineRoutine(0x00442e00)]
    public Angle GetFrameDirection(Angle direction) => Directions != 0 ? direction.GetFrameDirection(Directions) : direction;

    internal static Vid CreateEmpty(Map map, KnownObject id, in GameObject gameObject, int spriteSpeed, ObjectLayer layer)
    {
        var vid = new Vid(map, id, gameObject, [default], spriteSpeed, 0, layer, null);
        vid.Half = new Vector2(gameObject.Size.X * 0.5f, gameObject.Size.Y * 0.5f);
        vid.Scale = Vector3.One;
        Array.Fill(vid._durations, spriteSpeed);

        return vid;
    }

    internal static Vid CreateDefault(Map map) => CreateEmpty(map, KnownObject.None, new GameObject(SpriteClass.Primitive, DefaultSize), Map.DefaultSpriteSpeed, ObjectLayer.Menu);

    [EngineRoutine(0x00413860)]
    internal virtual Vid CreateMirror()
    {
        var mirror = CreateDefault(_map);
        mirror._weapons = _weapons;

        return mirror;
    }

    [EngineRoutine(0x004138b0)]
    internal virtual void Destruct()
    {
        int count = _counts[3] + _counts[2] + _counts[1] + _counts[0];
        if (count != 0)
        {
            Report(LogError.Text, count, "Not all sprites with this VID deleted"u8);
        }

        var next = NextMirror;
        if (next == this)
            return;

        var previous = next;
        for (var mirror = next.NextMirror; mirror != this; mirror = mirror.NextMirror)
        {
            previous = mirror;
        }

        previous.NextMirror = next;
    }

    [EngineRoutine(0x00413a30)]
    internal void Report(LogError code, int value, ReadOnlySpan<byte> text) =>
        _map.Session.Error.Error($"VID [{(int)Id}-{Name}]", code, value, text);

    [EngineRoutine(0x00413c30)]
    internal void LoadParameters(in GameObject record)
    {
        var sound = _map.Session.Sound;
        CopyRecord(record);
        ObjectGamma = record.Gamma.Pack();
        if (FileType.HasFlag(VidType.ZBuffer) && FileType.HasFlag(VidType.Hardware))
        {
            Scale = Vector3.One;
        }

        RotationRate = RotationRate.IsExactly(Unlimited) ? 0 : RotationRate.IsExactlyZero() ? Unlimited : Angle.FullTurn / RotationRate;
        Speed = ScaleRate(Speed, PerMillisecond);
        ZSpeed = ScaleRate(ZSpeed, PerMillisecond);
        Acceleration = ScaleRate(Acceleration, PerMillisecondSquared);
        Deceleration = ScaleRate(Deceleration, PerMillisecondSquared);
        NotCreatedAsChild = 0;
        CanMove = !Speed.IsExactlyZero() || !ZSpeed.IsExactlyZero() || Properties.HasFlag(ObjectProperties.Gravity) || Properties.HasFlag(ObjectProperties.Gravity2) || Properties.HasFlag(ObjectProperties.Wind);
        if (Directions == 0)
        {
            Report(LogError.Invalid, 0, "NoDir==0"u8);
            throw new InvalidDataException($"The record of object {(int)Id} doesn't have any directions.");
        }

        for (int state = 0; state < StateCount; state++)
        {
            if (_durations[state] == 0)
            {
                _durations[state] = SpriteSpeed;
            }
        }

        Half = Size.AsVector2() * 0.5f;
        DirectionOffset = Angle.HalfTurn / Directions;
        int frames = (short)TotalFrames;
        if (frames == 0)
        {
            Report(LogError.Invalid, 0, "noCadr==0"u8);
        }
        else if (frames < Directions)
        {
            Report(LogError.Invalid, 0, "noCadr < noDir"u8);
            Directions = frames;
        }

        int end = 0;
        for (int state = 0; state < StateCount; state++)
        {
            end += _frameCounts[state] * Directions;
        }

        if (frames < end)
        {
            Report(LogError.Missing, 0, "noCadr for noAnimCadr and noDir"u8);
            for (int state = StateCount - 1; state >= 0; state--)
            {
                int before = end - Directions * _frameCounts[state];

                if (before <= frames)
                {
                    _frameCounts[state] -= (end - frames) / Directions;
                    break;
                }

                _frameCounts[state] = 0;
                end = before;
            }
        }
        int start = 0;
        int firstPopulated = -1;
        for (int state = 0; state < StateCount; state++)
        {
            if (sound is not null && sound.HasSfx && !sound.IsValid(_sounds[state]) && Id != KnownObject.None)
            {
                Report(LogError.Invalid, _sounds[state], "sfx"u8);
                _sounds[state] = 0;
            }

            if (_frameCounts[state] == 0)
            {
                int borrowed = firstPopulated + 1;

                if (Class == SpriteClass.Frame && (state & 1) != 0 && state < (int)SpriteAnimation.Fight && _frameCounts[borrowed] != 0)
                {
                    _firstFrames[state] = _firstFrames[borrowed];
                    _framesPerDirection[state] = _framesPerDirection[borrowed];
                }
                else
                {
                    _firstFrames[state] = 0;
                    _framesPerDirection[state] = firstPopulated < 0 ? _firstFrames[StateCount - 1] : _framesPerDirection[firstPopulated];
                }
            }
            else
            {
                _firstFrames[state] = start;
                _framesPerDirection[state] = _frameCounts[state];
            }

            if (_frameCounts[state] != 0 && firstPopulated < 0)
            {
                firstPopulated = state;
                for (int previous = 0; previous < state; previous++)
                {
                    if (_framesPerDirection[previous] == 0)
                    {
                        _framesPerDirection[previous] = _frameCounts[state];
                    }
                }
            }

            start += _frameCounts[state] * Directions;

            if (frames < start)
            {
                Report(LogError.Text, state, "noCadr and noAnimCadr and noDir"u8);
                _firstFrames[state] = 0;
                _framesPerDirection[state] = firstPopulated < 0 ? _firstFrames[StateCount - 1] : _framesPerDirection[firstPopulated];
            }
        }
        if (Class == SpriteClass.BuiltTerrain)
        {
            IsBuiltTerrainClass = true;

            if (_map.Session.HasAppMenu)
            {
                Class = SpriteClass.Terrain;
            }
        }

        if (Properties.HasFlag(ObjectProperties.BuildSizeToGridZ))
        {
            var size = Size;
            var half = new Vector2(size.X, size.Y) * 0.5f;
            HeightPoints.Clear();
            _heightFrameStarts = default;
            int row = 0;
            float y = 0;

            while (y < size.Y)
            {
                int column = 0;
                float x = 0;

                while (x < size.X)
                {
                    column += 8;
                    HeightPoints.Add(new Vector3(x - half.X, y - half.Y, size.Z));
                    x = column;
                }

                row += 8;
                HeightPoints.Add(new Vector3(half.X, y - half.Y, size.Z));
                y = row;
            }

            int bottom = 0;

            if (0 < size.X)
            {
                float x = 0;

                do
                {
                    bottom += 8;
                    HeightPoints.Add(new Vector3(x - half.X, half.Y, size.Z));
                    x = bottom;
                }
                while (x < size.X);
            }

            HeightPoints.Add(new Vector3(half.X, half.Y, size.Z));
        }
    }

    [EngineRoutine(0x004145b0)]
    internal void SetGridZ(Sprite sprite)
    {
        var map = sprite.Map;
        if (sprite == map.Mouse || HeightPoints.Count == 0)
            return;

        bool permanent = !map.HasFlag(MapFlags.AppMenu) && Class == SpriteClass.BuiltTerrain;

        foreach (var point in GetHeightPoints(sprite.Frame))
        {
            var cell = new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z);

            if (permanent)
            {
                map.SetGroundZ(cell);
            }
            else
            {
                map.SetTempGroundZ(cell);
            }
        }
    }

    [EngineRoutine(0x004146b0)]
    internal void ResetGridZ(Sprite sprite)
    {
        var map = sprite.Map;
        if (sprite == map.Mouse || HeightPoints.Count == 0 || !map.HasFlag(MapFlags.AppMenu) && Class == SpriteClass.BuiltTerrain)
            return;

        foreach (var point in GetHeightPoints(sprite.Frame))
        {
            map.ClearTempGroundZ(new Vector3(sprite.Position.X + point.X, sprite.Position.Y + point.Y, sprite.Position.Z + point.Z));
        }
    }

    [EngineRoutine(0x00414890)]
    internal void SetHpCoeff(int army, int percent)
    {
        army &= 3;
        for (var vid = this; vid is not null; vid = vid.LinkVid)
        {
            int previous = vid.ArmyMaxHp[army];
            if (percent >= 0)
            {
                vid.ArmyMaxHp[army] = vid.MaxHp * percent / Map.Percent;
            }

            if (vid.MaxHp == 0)
                continue;

            var list = _map.GetLayer((int)vid.Layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];
                if (sprite is not null && sprite.Vid == vid && ((int)sprite.Army & 3) == army)
                {
                    sprite.ChangeHp(sprite.Hp * vid.ArmyMaxHp[army] * CoefficientScale / previous / CoefficientScale);
                }
            }
        }
    }

    [EngineRoutine(0x00414990)]
    internal void SetMaxHp(int army, int maxHp)
    {
        army &= 3;
        for (var vid = this; vid is not null; vid = vid.LinkVid)
        {
            int previous = vid.ArmyMaxHp[army];
            if (maxHp >= 0)
            {
                vid.ArmyMaxHp[army] = maxHp;
            }

            if (vid.MaxHp == 0)
                continue;

            var list = _map.GetLayer((int)vid.Layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];
                if (sprite is not null && sprite.Vid == vid && ((int)sprite.Army & 3) == army)
                {
                    sprite.ChangeHp(sprite.Hp * vid.ArmyMaxHp[army] * HpScale / previous / HpScale);
                }
            }
        }
    }

    [EngineRoutine(0x0043a1c0)]
    internal int RealDirection(Angle direction) => ((DirectionOffset + direction.Value) & 0xff) * Directions >> 8;

    internal void SetWeapons(AlienShooterWeaponRecord[] weapons) => _weapons = weapons;

    internal int GetFrameCount(SpriteAnimation state) => _frameCounts[(int)state];

    internal int GetFirstFrame(SpriteAnimation state) => _firstFrames[(int)state];

    internal int GetFramesPerDirection(SpriteAnimation state) => _framesPerDirection[(int)state];

    internal virtual void Load(in VidResource file, ReadOnlyMemory<byte> data)
    {
    }

    internal virtual void SetGamma(Gamma gamma, int army) => StoreGamma(gamma, army);

    internal ReadOnlySpan<Vector3> GetHeightPoints(int frame)
    {
        int start = 0;
        int end = HeightPoints.Count;

        if (!_heightFrameStarts.IsEmpty)
        {
            start = frame < TotalFrames ? _heightFrameStarts.Span[frame] : 0;

            if (frame < TotalFrames - 1)
            {
                end = _heightFrameStarts.Span[frame + 1];
            }
        }

        return start < end ? CollectionsMarshal.AsSpan(HeightPoints)[start..end] : [];
    }

    [EngineRoutine(0x00414570)]
    private protected void StoreGamma(Gamma gamma, int army)
    {
        if ((uint)army < ArmyCount)
        {
            _gammas[army] = gamma;
            return;
        }

        if (army != AllArmies)
        {
            Report(LogError.Invalid, army, "n_gamma in VID::SetGamma"u8);
        }
    }

    private protected void CopyHeights(Vid source)
    {
        HeightPoints.AddRange(source.HeightPoints);
        _heightFrameStarts = source._heightFrameStarts;
    }

    private protected Gamma GetGamma(Sprite sprite)
    {
        var gamma = ObjectGamma.Add(sprite.GetGamma());
        return Properties.HasFlag(ObjectProperties.Gamma) ? gamma : gamma.Add(_map.Session.Graphics.Gamma);
    }

    private static float ScaleRate(float rate, float scale) => rate.IsExactly(Unlimited) ? rate : rate * scale;

    private int GetAnimationTime(SpriteAnimation animation) =>
        _frameCounts[(int)animation] * (_durations[(int)animation] == 0 ? SpriteSpeed : _durations[(int)animation]);

    private void CopyRecord(in GameObject record)
    {
        SpriteType = record.SpriteType.ToOldSpriteType();
        Class = record.SpriteClass;
        Properties = record.Properties;
        MoveMask = record.MoveMask;
        Size = record.Size;
        MaxHp = record.MaxHp;
        Speed = record.MaxSpeed;
        ZSpeed = record.MaxZSpeed;
        Acceleration = record.Acceleration;
        Deceleration = record.Deceleration;
        RotationRate = record.RotationPeriod;
        WeaponIndex = record.WeaponIndex;
        DeathRange = record.DeathRange;
        Damage = record.DeathDamage;
        LinkOffset = record.LinkOffset;
        Link = record.Link;
        TopZ = record.TopZ;
        StepHeight = record.StepHeight;
        DropHeight = record.DropHeight;
        Lifetime = record.Lifetime;
        Directions = record.Directions;
        for (int state = 0; state < StateCount; state++)
        {
            _frameCounts[state] = record.FrameCounts[state];
            _sounds[state] = record.Sounds[state];
            _durations[state] = record.FrameDurations[state];
            _childX[state] = record.ChildX[state];
            _childY[state] = record.ChildY[state];
            _childZ[state] = record.ChildZ[state];
            _children[state] = record.Children[state];
            _childCounts[state] = record.ChildCounts[state];
        }

        Scale = record.Scale;
    }

    private void SetFlag(VidFlags flag, bool isSet) => _flags = (isSet ? flag : VidFlags.None) | _flags & ~flag;
}

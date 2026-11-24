using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a sprite definition.
/// </summary>
/// <remarks>
/// Definitions loaded from a sprite file are of one of the derived classes, chosen by the file's flags.
/// </remarks>
public class SpriteDefinition
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
    private const float BallisticRise = 1.1f;
    private const float BallisticDrop = 0.9f;

    private readonly int[] _children = new int[StateCount];
    private readonly int[] _childCounts = new int[StateCount];
    private readonly int[] _sounds = new int[StateCount];
    private readonly int[] _frameCounts = new int[StateCount];
    private readonly int[] _firstFrames = new int[StateCount];
    private readonly int[] _framesPerDirection = new int[StateCount];
    private readonly int[] _durations = new int[StateCount];
    private readonly int[] _counts = new int[ArmyCount];
    private readonly int[] _killed = new int[ArmyCount];
    private readonly int[] _recolored = new int[ArmyCount];
    private readonly int[] _armyMaxHp = new int[ArmyCount];
    private readonly int[] _armyMaxUnits = new int[ArmyCount];
    private readonly Gamma[] _gammas = new Gamma[ArmyCount];

    private GameObject _object;
    private WeaponRecord _weapon;
    private ReadOnlyMemory<int> _heightFrameStarts;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteDefinition"/> class from an object record and loads its parameters.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="spriteSpeed">The frame duration of the definition's sprite.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    /// <param name="layer">The update list of the definition.</param>
    /// <exception cref="InvalidDataException">The record doesn't have any directions.</exception>
    public SpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, int spriteSpeed, int totalFrames = 0, ObjectLayer layer = ObjectLayer.Ground)
        : this(id, gameObject, weapon, spriteSpeed, totalFrames, layer, null)
    {
        LoadParameters();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteDefinition"/> class from an object record and the header of its sprite file, without loading its parameters.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="file">The sprite file.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    protected SpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, in VidResource file, int totalFrames)
        : this(id, gameObject, weapon, file.Speed, totalFrames, ObjectLayer.Ground, file)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteDefinition"/> class from an object record without loading its parameters.
    /// </summary>
    /// <param name="id">The object ID.</param>
    /// <param name="gameObject">The object record.</param>
    /// <param name="weapon">The weapon record.</param>
    /// <param name="spriteSpeed">The frame duration of the definition's sprite.</param>
    /// <param name="totalFrames">The frame count of the definition's sprite.</param>
    /// <param name="layer">The update list of the definition.</param>
    /// <param name="file">The sprite file, or <see langword="null"/> without one.</param>
    protected SpriteDefinition(KnownObject id, in GameObject gameObject, in WeaponRecord weapon, int spriteSpeed, int totalFrames, ObjectLayer layer, VidResource? file)
    {
        Id = id;
        RecordId = id;
        Identity = this;
        _object = gameObject;
        _weapon = weapon;
        TotalFrames = totalFrames;
        Directions = gameObject.Directions;
        Class = gameObject.SpriteClass;

        SpriteSpeed = spriteSpeed;
        Layer = layer;
        ApplyRecord(gameObject, weapon);
        if (file is not null)
        {
            FileType = file.Value.Type;
            FrameSize = file.Value.Size;
            FileFrameCount = file.Value.FrameCount;
        }

        ResetLevelState();
    }

    /// <summary>
    /// Gets the object record of the definition.
    /// </summary>
    public ref readonly GameObject Object => ref _object;

    /// <summary>
    /// Gets the weapon record of the definition.
    /// </summary>
    public ref readonly WeaponRecord Weapon => ref _weapon;

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
    /// Gets or sets the object ID that sprites of the definition report.
    /// </summary>
    public KnownObject Id { get; set; }

    /// <summary>
    /// Gets the ID of the record the definition was loaded from.
    /// </summary>
    public KnownObject RecordId { get; }

    /// <summary>
    /// Gets the name of the object the definition was loaded from.
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the path of the sprite file the definition was loaded from.
    /// </summary>
    public string Path { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the sound ID of each animation state, with an invalid ID in the record replaced by the first sound.
    /// </summary>
    public ReadOnlySpan<int> Sounds => _sounds;

    /// <summary>
    /// Gets or sets the definition that sprites of this definition are created as.
    /// </summary>
    public SpriteDefinition Identity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether sprites of the definition are hidden.
    /// </summary>
    [EngineRoutine(0x0043a1b0)]
    public bool IsHidden { get; set; }

    /// <summary>
    /// Gets the speed in pixels per second, the unit level scripts use.
    /// </summary>
    public int ScriptSpeed => Speed.IsExactly(Unlimited) ? Unlimited : (int)(Speed * 1000);

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates color, scale, offset or rotation.
    /// </summary>
    public bool HasKeyframes { get; set; }

    /// <summary>
    /// Gets or sets the wall time at which a sprite of the definition was last created or recounted.
    /// </summary>
    public uint LastCreatedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the color.
    /// </summary>
    public bool HasColorKeyframes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the scale.
    /// </summary>
    public bool HasScaleKeyframes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the weapon record animates the offset.
    /// </summary>
    public bool HasOffsetKeyframes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether sprites of the definition carry a lifetime, a keyframe track, a stream period and a top speed of their own.
    /// </summary>
    public bool HasTimers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a region can replace the definition of sprites created from it.
    /// </summary>
    public bool IsRegionReplaced { get; set; }

    /// <summary>
    /// Gets the sprite class of the definition.
    /// </summary>
    public SpriteClass Class { get; private set; }

    /// <summary>
    /// Gets the gamma that the definition applies to its sprites.
    /// </summary>
    public Gamma ObjectGamma { get; private set; }

    /// <summary>
    /// Gets the factors that scale the sprites of the definition.
    /// </summary>
    public Vector3 Scale { get; private set; }

    /// <summary>
    /// Gets the steps of a full turn that the sprites of the definition make per millisecond; 0 turns never and 999999 turns at once.
    /// </summary>
    public float RotationRate { get; private set; }

    /// <summary>
    /// Gets the frame duration of the definition's sprite.
    /// </summary>
    public int SpriteSpeed { get; private set; }

    /// <summary>
    /// Gets the width and height of a frame of the sprite file, in pixels.
    /// </summary>
    public Size FrameSize { get; protected set; }

    /// <summary>
    /// Gets the number of frames in the definition's sprite file, or 0 if there isn't a sprite file.
    /// </summary>
    public short FileFrameCount { get; protected set; }

    /// <summary>
    /// Gets the weapon flags of the definition.
    /// </summary>
    public WeaponFlags WeaponFlags { get; private set; }

    /// <summary>
    /// Gets the behavior bits of the definition.
    /// </summary>
    public WeaponBehavior Behavior { get; private set; }

    /// <summary>
    /// Gets the army of the definition.
    /// </summary>
    public Army Army { get; private set; }

    /// <summary>
    /// Gets the rank of the definition.
    /// </summary>
    public int Rank { get; private set; }

    /// <summary>
    /// Gets the sprite types the definition attacks.
    /// </summary>
    public SpriteType Targets { get; private set; }

    /// <summary>
    /// Gets a value indicating whether other sprites push sprites of the definition aside.
    /// </summary>
    public bool CanBePushed => WeaponFlags.HasFlag(WeaponFlags.Pushable);

    /// <summary>
    /// Gets a value indicating whether the definition attacks before it faces its target.
    /// </summary>
    public bool CanAttackWhileTurning => WeaponFlags.HasFlag(WeaponFlags.AttacksWhileTurning);

    /// <summary>
    /// Gets a value indicating whether the definition attacks only targets ahead of it.
    /// </summary>
    public bool HasForwardTargets => WeaponFlags.HasFlag(WeaponFlags.ForwardTargets);

    /// <summary>
    /// Gets a value indicating whether the definition alternates the two children of a mirrored pair.
    /// </summary>
    public bool HasDoubleAttack => WeaponFlags.HasFlag(WeaponFlags.DoubleAttack);

    /// <summary>
    /// Gets the state a sprite of the definition starts in.
    /// </summary>
    public SpriteAnimation InitialAnimation =>
        GetFrameCount(SpriteAnimation.Stand) == 0 && GetFrameCount(SpriteAnimation.Go) == 0 &&
        GetFrameCount(SpriteAnimation.Death) != 0 ? SpriteAnimation.Death
        : GetFrameCount(SpriteAnimation.Birth) != 0 ? SpriteAnimation.Birth : SpriteAnimation.Stand;

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
    public ObjectLayer Layer { get; protected set; }

    /// <summary>
    /// Gets the vertical speed of the definition in pixels per millisecond.
    /// </summary>
    public float ZSpeed { get; private set; }

    /// <summary>
    /// Gets the acceleration of the definition.
    /// </summary>
    public float Acceleration { get; private set; }

    /// <summary>
    /// Gets the deceleration of the definition.
    /// </summary>
    public float Deceleration { get; private set; }

    /// <summary>
    /// Gets half the footprint of the definition.
    /// </summary>
    public Vector2 Half { get; private set; }

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
    /// Gets the gamma the definition's sprites are drawn with per army.
    /// </summary>
    public Span<Gamma> Gammas => _gammas;

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
    public bool ShouldSkipOwnArmy => Object.Properties.HasFlag(ObjectProperties.NoFriendlyDamage);

    /// <summary>
    /// Gets or sets the range at which the definition sees enemies.
    /// </summary>
    public float DetectRange { get; set; }

    /// <summary>
    /// Gets the range at which the definition attacks.
    /// </summary>
    public float BattleRange { get; private set; }

    /// <summary>
    /// Gets the range under which the definition doesn't attack.
    /// </summary>
    public float MinRange { get; private set; }

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
    public int ReloadTime { get; private set; }

    /// <summary>
    /// Gets the time an attack of the definition takes.
    /// </summary>
    public int AttackTime { get; private set; }

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

    /// <summary>
    /// Gets the height points of the definition, those of the size grid followed by those of the sprite file's frames.
    /// </summary>
    public List<Vector3> HeightPoints { get; } = [];

    /// <summary>
    /// Gets or sets the flags of the sprite file.
    /// </summary>
    public VidType FileType { get; set; }

    /// <summary>
    /// Gets or sets the flag that keeps the definition from being created as a child.
    /// </summary>
    public int NotCreatedAsChild
    {
        [EngineRoutine(0x0043a300)]
        get;
        [EngineRoutine(0x0043a310)]
        set;
    }

    internal bool IsBuiltTerrainClass { get; private set; }

    internal bool CanMove { get; private set; }

    internal bool IsCopy { get; set; }

    internal int Directions { get; private set; }

    internal int DirectionOffset { get; private set; }

    internal int TotalFrames { get; }

    /// <summary>
    /// Sets where the height points of each frame of the sprite file start in <see cref="HeightPoints"/>.
    /// </summary>
    /// <param name="starts">The index of the first height point of each frame.</param>
    public void SetHeightFrameStarts(ReadOnlyMemory<int> starts) => _heightFrameStarts = starts;

    /// <summary>
    /// Loads the data of the sprite file into the definition.
    /// </summary>
    /// <param name="file">The sprite file.</param>
    /// <param name="data">The bytes of the sprite file.</param>
    public void Load(in VidResource file, ReadOnlyMemory<byte> data) => Load(file, data, null);

    /// <summary>
    /// Draws a sprite with the frames of this definition.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite that gives the position, frame and gamma; it may belong to another definition.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    public virtual void Draw(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
    }

    /// <summary>
    /// Draws the outline of the frame of a sprite, stored as points in the frame.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite that gives the position and frame.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    public virtual void DrawOutline(Screen screen, Map map, Sprite sprite, Vector2 camera)
    {
    }

    /// <summary>
    /// Draws a sprite of this definition into the textures of a tile of the hardware ground.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    /// <param name="tile">The tile of the hardware ground.</param>
    /// <param name="colorTexture">The color texture of <paramref name="tile"/>.</param>
    /// <param name="depthTexture">The depth texture of <paramref name="tile"/>.</param>
    /// <param name="tileViewport">The area of the textures that is drawn to, in texels.</param>
    public virtual void DrawToVid(Map map, Sprite sprite, HardwareTile tile, TextureData colorTexture, TextureData depthTexture, Rectangle tileViewport)
    {
    }

    /// <summary>
    /// Draws a built terrain sprite into the textures of this definition.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="sprite">The sprite.</param>
    public virtual void AddVidToTextures(Map map, Sprite sprite)
    {
    }

    /// <summary>
    /// Sets the update list of the definition.
    /// </summary>
    [EngineRoutine(0x00413880)]
    public virtual void SetLayer() => Layer = ObjectLayer.Ground;

    /// <summary>
    /// Reloads the values the level script can change from the records.
    /// </summary>
    /// <exception cref="InvalidDataException">The record doesn't have any directions.</exception>
    public void LoadParameters() => LoadParameters(null, null, false);

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
    /// Resets the per-army counts and limits and unbinds every script handler for a new level.
    /// </summary>
    [EngineRoutine(0x00413980)]
    public void ResetLevelState()
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
    /// Returns the frame duration of a state.
    /// </summary>
    /// <param name="animation">The state.</param>
    /// <returns>The duration in milliseconds.</returns>
    public int GetVisualFrameDuration(SpriteAnimation animation) => _durations[(int)animation];

    /// <summary>
    /// Returns the length of a state's animation.
    /// </summary>
    /// <param name="animation">The state.</param>
    /// <returns>The length in milliseconds.</returns>
    public int GetAnimationDuration(SpriteAnimation animation) => Math.Max(1, Object.FrameCounts[(int)animation]) * GetVisualFrameDuration(animation);

    /// <summary>
    /// Returns the vertical speed that carries a launched sprite of this definition up or down a rise over a distance, within its vertical speed limit.
    /// </summary>
    /// <param name="catalog">The sprite catalog.</param>
    /// <param name="rise">The height to climb, negative to drop.</param>
    /// <param name="distance">The horizontal distance to the goal.</param>
    /// <returns>The vertical speed in pixels per millisecond.</returns>
    [EngineRoutine(0x00414a80)]
    public float CalculateZSpeed(SpriteCatalog catalog, float rise, float distance)
    {
        ref readonly var gameObject = ref Object;
        bool hasGravity = gameObject.Properties.HasFlag(ObjectProperties.Gravity);
        bool hasGravity2 = !hasGravity && gameObject.Properties.HasFlag(ObjectProperties.Gravity2);
        float speed;

        if (hasGravity || hasGravity2)
        {
            float gravity = hasGravity ? catalog.Gravitation : catalog.Gravitation2;
            speed = rise * Speed / distance + distance * gravity / Speed * 0.5f;
            speed *= speed > 0 ? BallisticRise : BallisticDrop;
        }
        else
        {
            speed = gameObject.Properties.HasFlag(ObjectProperties.SelfMoving) ? ZSpeed : gameObject.TopZ.IsExactlyZero() ? rise * Speed / distance : 0;
        }

        if (speed > ZSpeed)
            return ZSpeed;

        return speed < -ZSpeed ? -ZSpeed : speed;
    }

    /// <summary>
    /// Returns the direction of the direction frame a sprite of this definition shows for a direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The frame's direction, or the direction itself if the definition does not use direction frames.</returns>
    [EngineRoutine(0x00442e00)]
    public Angle GetFrameDirection(Angle direction) => Directions != 0 ? direction.GetFrameDirection(Directions) : direction;

    /// <summary>
    /// Returns where a link sits relative to a sprite of this definition facing a direction.
    /// </summary>
    /// <param name="link">The link's definition.</param>
    /// <param name="direction">The direction of the sprite.</param>
    /// <returns>The offset from the sprite's position.</returns>
    public Vector3 GetLinkOffset(SpriteDefinition link, Angle direction) => Object.GetLinkOffset(link.Object, null, 0, direction);

    /// <summary>
    /// Returns the number of living sprites of the definition in an army.
    /// </summary>
    /// <param name="army">The army, from 0 to 3.</param>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a250)]
    public int GetCount(int army) => _counts[army];

    /// <summary>
    /// Returns the number of living sprites of the definition in all armies.
    /// </summary>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a260)]
    public int GetTotalCount() => _counts[3] + _counts[2] + _counts[1] + _counts[0];

    /// <summary>
    /// Returns the number of killed sprites of the definition in an army.
    /// </summary>
    /// <param name="army">The army, masked to 0 to 3.</param>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a280)]
    public int GetKilled(int army) => _killed[army & ArmyMask];

    /// <summary>
    /// Returns the number of killed sprites of the definition in all armies.
    /// </summary>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a2a0)]
    public int GetTotalKilled() => _killed[3] + _killed[2] + _killed[1] + _killed[0];

    /// <summary>
    /// Returns the number of recolored sprites of the definition in an army.
    /// </summary>
    /// <param name="army">The army, masked to 0 to 3.</param>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a2c0)]
    public int GetRecolored(int army) => _recolored[army & ArmyMask];

    /// <summary>
    /// Returns the number of recolored sprites of the definition in all armies.
    /// </summary>
    /// <returns>The count.</returns>
    [EngineRoutine(0x0043a2e0)]
    public int GetTotalRecolored() => _recolored[3] + _recolored[2] + _recolored[1] + _recolored[0];

    /// <summary>
    /// Returns the health of a new sprite of the definition for an army.
    /// </summary>
    /// <param name="army">The army, masked to 0 to 3.</param>
    /// <returns>The health.</returns>
    [EngineRoutine(0x0043a230)]
    public int GetArmyMaxHp(int army) => _armyMaxHp[army & ArmyMask];

    /// <summary>
    /// Determines whether the definition has a fight child and a weapon.
    /// </summary>
    /// <returns><see langword="true"/> if the definition is armed with a weapon of its own; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0043a1e0)]
    public bool HasFightWeapon() => FightChild != 0 && Object.WeaponIndex != 0;

    /// <summary>
    /// Determines whether the object of the definition is born as smoke.
    /// </summary>
    /// <returns><see langword="true"/> if the object is born as smoke; otherwise, <see langword="false"/>.</returns>
    [EngineRoutine(0x0043a320)]
    public bool IsBirthAsSmoke() => Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke);

    /// <summary>
    /// Hides or shows the sprites of the definition and of every definition linked to it.
    /// </summary>
    /// <param name="hidden"><see langword="true"/> to hide the sprites.</param>
    [EngineRoutine(0x00414840)]
    public void SetPropHide(bool hidden)
    {
        IsHidden = hidden;
        for (var link = LinkDefinition; link is not null; link = link.LinkDefinition)
        {
            link.IsHidden = hidden;
        }
    }

    [EngineRoutine(0x00413710)]
    internal static SpriteDefinition CreateEmpty(KnownObject id, in GameObject gameObject, int spriteSpeed, ObjectLayer layer)
    {
        var definition = new SpriteDefinition(id, gameObject, default, spriteSpeed, 0, layer, null);
        definition.Half = new Vector2(gameObject.Size.X * 0.5f, gameObject.Size.Y * 0.5f);
        definition.Scale = Vector3.One;
        Array.Fill(definition._durations, spriteSpeed);

        return definition;
    }

    internal void ReplaceRecord(in GameObject gameObject, in WeaponRecord weapon)
    {
        _object = gameObject;
        _weapon = weapon;
        ApplyRecord(gameObject, weapon);
    }

    internal int GetDirectionIndex(Angle direction) => direction.GetDirectionIndex(Directions, DirectionOffset);

    internal int GetFrameCount(SpriteAnimation state) => _frameCounts[(int)state];

    internal int GetFirstFrame(SpriteAnimation state) => _firstFrames[(int)state];

    internal int GetFramesPerDirection(SpriteAnimation state) => _framesPerDirection[(int)state];

    internal virtual void Load(in VidResource file, ReadOnlyMemory<byte> data, GameLogger? logger)
    {
    }

    internal virtual void SetGamma(Gamma gamma, int army, Gamma ambient, GameLogger? logger) => StoreGamma(gamma, army, logger);

    [EngineRoutine(0x00413c30)]
    internal void LoadParameters(GameLogger? logger, SoundSystem? sound, bool resetBuiltTerrain)
    {
        CopyRecordParameters();
        if (Directions == 0)
        {
            Report(logger, LogError.Invalid, 0, "NoDir==0"u8);
            throw new InvalidDataException($"The record of object {(int)Id} doesn't have any directions.");
        }

        for (int state = 0; state < StateCount; state++)
        {
            if (_durations[state] == 0)
            {
                _durations[state] = SpriteSpeed;
            }
        }

        Half = _object.Size.AsVector2() * 0.5f;
        DirectionOffset = Angle.HalfTurn / Directions;
        int frames = (short)TotalFrames;
        if (frames == 0)
        {
            Report(logger, LogError.Invalid, 0, "noCadr==0"u8);
        }
        else if (frames < Directions)
        {
            Report(logger, LogError.Invalid, 0, "noCadr < noDir"u8);
            Directions = frames;
        }

        TrimFrameCounts(logger, frames);
        AssignFirstFrames(logger, sound, frames);
        if (Class == SpriteClass.BuiltTerrain)
        {
            IsBuiltTerrainClass = true;

            if (resetBuiltTerrain)
            {
                Class = SpriteClass.Terrain;
            }
        }

        if (_object.Properties.HasFlag(ObjectProperties.BuildSizeToGridZ))
        {
            BuildHeightPoints();
        }
    }

    [EngineRoutine(0x00413a30)]
    internal void Report(GameLogger? logger, LogError code, int value, ReadOnlySpan<byte> text) =>
        logger?.Error($"VID [{(int)Id}-{Name}]", code, value, text);

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

    internal CollisionVolume GetCollisionVolume(Vector3 position) => new(position, Half, Object.Size.Z, MoveMask);

    [EngineRoutine(0x00414570)]
    private protected void StoreGamma(Gamma gamma, int army, GameLogger? logger)
    {
        if ((uint)army < ArmyCount)
        {
            _gammas[army] = gamma;
            return;
        }

        if (army != AllArmies)
        {
            Report(logger, LogError.Invalid, army, "n_gamma in VID::SetGamma"u8);
        }
    }

    private protected Gamma GetGamma(Map map, Sprite sprite)
    {
        var gamma = ObjectGamma.Add(sprite.GetGamma());
        return Object.Properties.HasFlag(ObjectProperties.Gamma) ? gamma : gamma.Add(map.Session.Graphics.Gamma);
    }

    private static int GetAnimationTime(in GameObject gameObject, SpriteAnimation animation, int spriteSpeed) =>
        gameObject.FrameCounts[(int)animation] * gameObject.GetFrameDuration(animation, spriteSpeed);

    private static float ScaleRate(float rate, float scale) => rate.IsExactly(Unlimited) ? rate : rate * scale;

    private void ApplyRecord(in GameObject gameObject, in WeaponRecord weapon)
    {
        WeaponFlags = weapon.Flags;
        Behavior = weapon.Behavior;
        Army = weapon.Army;
        Rank = weapon.Rank;
        Targets = weapon.Targets;
        BattleRange = weapon.BattleRange;
        MinRange = weapon.MinRange;
        ReloadTime = weapon.ReloadTime;
        AttackTime = Math.Max(weapon.ReloadTime, GetAnimationTime(gameObject, SpriteAnimation.Fight, SpriteSpeed));
    }

    private void CopyRecordParameters()
    {
        Class = _object.SpriteClass;
        MaxHp = _object.MaxHp;
        ObjectGamma = _object.Gamma.Pack();
        Scale = FileType.HasFlag(VidType.ZBuffer) && FileType.HasFlag(VidType.Hardware) ? Vector3.One : _object.Scale;
        RotationRate = _object.RotationPeriod.IsExactly(Unlimited) ? 0 : _object.RotationPeriod.IsExactlyZero() ? Unlimited : Angle.FullTurn / _object.RotationPeriod;
        Speed = ScaleRate(_object.MaxSpeed, PerMillisecond);
        ZSpeed = ScaleRate(_object.MaxZSpeed, PerMillisecond);
        Acceleration = ScaleRate(_object.Acceleration, PerMillisecondSquared);
        Deceleration = ScaleRate(_object.Deceleration, PerMillisecondSquared);
        MoveMask = _object.MoveMask;
        Damage = _object.DeathDamage;
        Lifetime = _object.Lifetime;
        DetectRange = _weapon.DetectRange;
        Aim = _weapon.Aim;
        AmmoCapacity = _weapon.Ammo;
        BuildTime = _weapon.BuildTime;
        NotCreatedAsChild = 0;
        Directions = _object.Directions;
        for (int state = 0; state < StateCount; state++)
        {
            _children[state] = _object.Children[state];
            _childCounts[state] = _object.ChildCounts[state];
            _sounds[state] = _object.Sounds[state];
            _frameCounts[state] = _object.FrameCounts[state];
            _durations[state] = _object.FrameDurations[state];
        }

        CanMove = !Speed.IsExactlyZero() || !ZSpeed.IsExactlyZero() || _object.Properties.HasFlag(ObjectProperties.Gravity) || _object.Properties.HasFlag(ObjectProperties.Gravity2) || _object.Properties.HasFlag(ObjectProperties.Wind);
    }

    private void TrimFrameCounts(GameLogger? logger, int frames)
    {
        int end = 0;
        for (int state = 0; state < StateCount; state++)
        {
            end += _frameCounts[state] * Directions;
        }

        if (frames < end)
        {
            Report(logger, LogError.Missing, 0, "noCadr for noAnimCadr and noDir"u8);
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
    }

    private void AssignFirstFrames(GameLogger? logger, SoundSystem? sound, int frames)
    {
        int start = 0;
        int firstPopulated = -1;
        for (int state = 0; state < StateCount; state++)
        {
            if (sound is not null && !sound.IsValid(_sounds[state]) && Id != KnownObject.None)
            {
                Report(logger, LogError.Invalid, _sounds[state], "sfx"u8);
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

                if (firstPopulated < 0)
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
            }

            start += _frameCounts[state] * Directions;

            if (frames < start)
            {
                Report(logger, LogError.Text, state, "noCadr and noAnimCadr and noDir"u8);
                _firstFrames[state] = 0;
                _framesPerDirection[state] = firstPopulated < 0 ? _firstFrames[StateCount - 1] : _framesPerDirection[firstPopulated];
            }
        }
    }

    private void BuildHeightPoints()
    {
        var size = _object.Size;
        var half = new Vector2(size.X, size.Y) * 0.5f;
        HeightPoints.Clear();
        _heightFrameStarts = default;
        int row = 0;
        float y = 0;

        if (0 < size.Y)
        {
            do
            {
                int column = 0;
                float x = 0;

                if (0 < size.X)
                {
                    do
                    {
                        column += 8;
                        HeightPoints.Add(new Vector3(x - half.X, y - half.Y, size.Z));
                        x = column;
                    }
                    while (x < size.X);
                }

                row += 8;
                HeightPoints.Add(new Vector3(half.X, y - half.Y, size.Z));
                y = row;
            }
            while (y < size.Y);
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

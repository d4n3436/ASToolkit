using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class GameWorld
{
    private static readonly (InputFlags Raw, ScriptInput Script)[] InputStateBits =
    [
        (InputFlags.LeftClick, ScriptInput.LeftClick),
        (InputFlags.RightClick, ScriptInput.RightClick),
        (InputFlags.LeftDown, ScriptInput.LeftDown),
        (InputFlags.RightDown, ScriptInput.RightDown),
        (InputFlags.Shift, ScriptInput.Shift),
        (InputFlags.Ctrl, ScriptInput.Ctrl),
        (InputFlags.Left, ScriptInput.Left),
        (InputFlags.Right, ScriptInput.Right),
        (InputFlags.Up, ScriptInput.Up),
        (InputFlags.Down, ScriptInput.Down),
        (InputFlags.First, ScriptInput.First),
        (InputFlags.Second, ScriptInput.Second)
    ];

    private const int ShakeReach = 4;

    private readonly List<Unit> _scripted = [];
    private readonly Dictionary<MergedMapSprite, Unit> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MergedMapSprite, Unit> _statics = new(ReferenceEqualityComparer.Instance);
    private readonly List<MergedMapSprite> _sprites;
    private bool _hasHardwareGround;

    /// <summary>
    /// Gets a value indicating whether the level script declared the mission complete.
    /// </summary>
    public bool HasWon => IsLevelWon();

    /// <summary>
    /// Gets the dynamite sites still standing.
    /// </summary>
    public IReadOnlyList<Unit> DynamiteSites => GetLiving(KnownObject.DynamiteSite, _livingSites);

    /// <summary>
    /// Gets the planted charges still burning.
    /// </summary>
    public IReadOnlyList<Unit> ArmedDynamite => GetLiving(KnownObject.ArmedDynamite, _livingCharges);

    /// <summary>
    /// Gets the bodies of placed sprites that take damage.
    /// </summary>
    public IEnumerable<Unit> Bodies => _bodies.Values;

    /// <summary>
    /// Gets the map's sprites and the ones created since the load.
    /// </summary>
    public IReadOnlyList<MergedMapSprite> Sprites => _sprites;

    /// <summary>
    /// Gets the number of sprites removed from the map.
    /// </summary>
    public int RemovedCount => _removed.Count;

    /// <summary>
    /// Returns whether a sprite was removed from the map.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns><see langword="true"/> when the sprite is gone; otherwise, <see langword="false"/>.</returns>
    public bool IsRemoved(MergedMapSprite sprite) => _removed.Contains(sprite);

    /// <summary>
    /// Returns the unit a map sprite became.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns>The unit, or null when the sprite has none.</returns>
    public Unit? FindPlacementUnit(MergedMapSprite sprite) =>
        _bodies.TryGetValue(sprite, out var body) ? body : _statics.GetValueOrDefault(sprite) ?? _scripted.Find(unit => unit.Placement == sprite);

    /// <summary>
    /// Gets the scripted sprites the world updates.
    /// </summary>
    public IReadOnlyList<Unit> Scripted => _scripted;

    private Vector3 GetPlacementPosition(MergedMapSprite sprite) =>
        _bodies.TryGetValue(sprite, out var body) ? body.Position : _statics.TryGetValue(sprite, out var stand) ? stand.Position : sprite.Position;

    private Unit? AddStatic(MergedMapSprite sprite, SpriteDefinition definition)
    {
        if (definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man or SpriteClass.Cannon)
            return null;

        var stand = ConstructSprite(definition, sprite.Position, sprite.Direction, sprite.Army, 0, UpdateRole.Sprite);
        stand.Placement = sprite;
        _statics[sprite] = stand;

        if (definition.Object.SpriteClass == SpriteClass.BuiltTerrain)
        {
            ConstructBuiltTerrain(stand);
        }

        return stand;
    }

    [EngineRoutine(0x0040edf0)]
    private void ConstructBuiltTerrain(Unit unit)
    {
        if (!_hasHardwareGround)
        {
            CreateHardwareGround();
        }

        if (IsBaked(unit.Definition))
        {
            unit.ChangeAnimation(SpriteAnimation.Death);
        }
    }

    private static bool IsBaked(SpriteDefinition definition) => definition.Object.SpriteClass == SpriteClass.BuiltTerrain && !UnitGrid.IsHashed(definition);

    // TODO: Port the hardware ground sprite
    [EngineRoutine(0x00412a40)]
    private void CreateHardwareGround()
    {
        _hasHardwareGround = true;
        _rng.Next();
    }

    [EngineRoutine(0x0043f500)]
    private void RemoveStatic(Unit unit)
    {
        if (unit.Placement is not null)
        {
            _statics.Remove(unit.Placement);
        }

        _builders.Remove(unit);
        Units.Remove(unit);
        Effects.Detach(unit);
        ReleaseSprite(unit);
    }

    private Unit AddScripted(SpriteDefinition definition, Vector3 position, Angle direction, Army? army = null, MergedMapSprite? placement = null)
    {
        var unit = ConstructSprite(definition, position, direction, army ?? Definitions.GetArmy(definition), 0, UpdateRole.Scripted);
        unit.Placement = placement;
        _scripted.Add(unit);

        return unit;
    }

    private bool TickScripted(Unit unit)
    {
        if ((unit.Owner is null || unit.Owner.Link == unit) && Tact(unit))
            return true;

        RemoveScripted(unit);

        return false;
    }

    private void RemoveScripted(Unit unit)
    {
        _scripted.Remove(unit);
        Units.Remove(unit);
        Effects.Detach(unit);

        if (unit.Placement is not null)
        {
            RemoveSprite(unit.Placement);
        }

        ReleaseSprite(unit);
    }

    /// <summary>
    /// Runs the creation handler of a sprite's definition.
    /// </summary>
    /// <param name="unit">The sprite.</param>
    [EngineRoutine(0x0040e870)]
    public void RunCreationHook(Unit unit) => RunCreationHandler(unit);

    /// <summary>
    /// Destroys the sprite of a definition nearest a point.
    /// </summary>
    /// <param name="id">The object id.</param>
    /// <param name="point">The point.</param>
    [EngineRoutine(0x00440830)]
    public void DestroySprite(KnownObject id, Vector2 point)
    {
        var sprite = FindSprite(id, point);

        if (sprite is not null)
        {
            Destroy(sprite);
        }
    }

    private void Destroy(MergedMapSprite sprite)
    {
        if (_bodies.Remove(sprite, out var body))
        {
            body.Hp = 0;
            _updates.Remove(body.Definition.Layer, body);
            Units.Remove(body);
            Effects.Detach(body);
            ReleaseSprite(body);
        }

        for (int i = _scripted.Count - 1; i >= 0; i--)
        {
            var unit = _scripted[i];

            if (ReferenceEquals(unit.Placement, sprite))
            {
                _updates.Remove(unit.Definition.Layer, unit);
                _scripted.RemoveAt(i);
                Units.Remove(unit);
                Effects.Detach(unit);
                ReleaseSprite(unit);
            }
        }

        RemoveSprite(sprite);
    }

    /// <summary>
    /// Gets a value indicating whether the player mans a cannon.
    /// </summary>
    public bool IsInCannon => Player.Definition.Id == KnownObject.MannedCannon;

    /// <summary>
    /// Gets a value indicating whether an invulnerability or rescue object protects the player.
    /// </summary>
    public bool IsProtected
    {
        get
        {
            foreach (var link in Player.EnumerateLinks())
            {
                if (link.Definition.Id is KnownObject.InvulnerabilityEffect or KnownObject.RescueMedkitEffect)
                    return true;
            }

            return false;
        }
    }

    private ScriptInput GetInputState()
    {
        var flags = Input.Flags;
        var state = ScriptInput.None;
        foreach (var (raw, script) in InputStateBits)
        {
            if ((flags & raw) != 0)
            {
                state |= script;
            }
        }

        return state;
    }

    private void RemoveSprite(MergedMapSprite sprite)
    {
        if (_removed.Contains(sprite))
            return;

        RemovePlacement(sprite);
    }

    [EngineRoutine(0x0040fc40)]
    private MergedMapSprite? FindSprite(KnownObject id, Vector2 point)
    {
        MergedMapSprite? found = null;
        float foundX = float.MaxValue;
        float foundY = float.MaxValue;
        foreach (var sprite in _sprites)
        {
            var definition = Definitions.Find(id);

            if (sprite.Id != id || _removed.Contains(sprite) || definition is null)
                continue;

            if (definition.GetCollisionVolume(GetPlacementPosition(sprite)).Covers(point) && (found is null || definition.Object.Size.X < foundX || definition.Object.Size.Y < foundY))
            {
                found = sprite;
                foundX = definition.Object.Size.X;
                foundY = definition.Object.Size.Y;
            }
        }

        return found;
    }

    [EngineRoutine(0x00431cf0)]
    private void SetEnvironment(MapEnvironment environment)
    {
        if (environment.HasFlag(MapEnvironment.Stop))
        {
            _environment &= ~environment;
            return;
        }

        if (environment is MapEnvironment.Shadow or MapEnvironment.LongShadow)
        {
            _environment &= ~(MapEnvironment.Shadow | MapEnvironment.LongShadow);
        }

        if ((environment & MapEnvironment.Rain) != 0)
        {
            _environment &= ~MapEnvironment.Rain;
        }

        if ((environment & MapEnvironment.Snow) != 0)
        {
            _environment &= ~MapEnvironment.Snow;
        }

        _environment |= environment;
    }

    [EngineRoutine(0x00430870)]
    [EngineRoutine(0x00430870)]
    private void ShakeCamera()
    {
        if (!_environment.HasFlag(MapEnvironment.Earthquake))
        {
            CameraShake = null;
            return;
        }

        int y = _rng.Next() % (ShakeReach * 2 + 1);
        int x = _rng.Next() % (ShakeReach * 2 + 1);
        CameraShake = new CameraShake(Time, new Vector2(ShakeReach - x, ShakeReach - y));
    }

    /// <summary>
    /// Gets the camera shake of the frame.
    /// </summary>
    public CameraShake? CameraShake { get; private set; }

    /// <summary>
    /// Gets the screen flash in progress.
    /// </summary>
    public ScreenFlash? ScreenFlash { get; private set; }
}

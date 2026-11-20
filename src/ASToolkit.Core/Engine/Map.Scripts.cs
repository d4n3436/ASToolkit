using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
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

    private readonly List<Sprite> _scripted = [];
    private readonly Dictionary<MergedMapSprite, Sprite> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MergedMapSprite, Sprite> _statics = new(ReferenceEqualityComparer.Instance);
    private readonly List<MergedMapSprite> _sprites;
    private bool _hasHardwareGround;

    /// <summary>
    /// Gets a value indicating whether the level script declared the mission complete.
    /// </summary>
    public bool HasWon => IsLevelWon();

    /// <summary>
    /// Gets the dynamite sites still standing.
    /// </summary>
    public IReadOnlyList<Sprite> DynamiteSites => GetLiving(KnownObject.DynamiteSite, _livingSites);

    /// <summary>
    /// Gets the planted charges still burning.
    /// </summary>
    public IReadOnlyList<Sprite> ArmedDynamite => GetLiving(KnownObject.ArmedDynamite, _livingCharges);

    /// <summary>
    /// Gets the bodies of placed sprites that take damage.
    /// </summary>
    public IEnumerable<Sprite> Bodies => _bodies.Values;

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
    /// <param name="placement">The sprite.</param>
    /// <returns><see langword="true"/> when the sprite is gone; otherwise, <see langword="false"/>.</returns>
    public bool IsRemoved(MergedMapSprite placement) => _removed.Contains(placement);

    /// <summary>
    /// Returns the sprite a placed map sprite became.
    /// </summary>
    /// <param name="placement">The sprite.</param>
    /// <returns>The sprite, or null when the sprite has none.</returns>
    public Sprite? FindPlacementSprite(MergedMapSprite placement) =>
        _bodies.TryGetValue(placement, out var body) ? body : _statics.GetValueOrDefault(placement) ?? _scripted.Find(sprite => sprite.Placement == placement);

    /// <summary>
    /// Gets the scripted sprites the map updates.
    /// </summary>
    public IReadOnlyList<Sprite> Scripted => _scripted;

    private Vector3 GetPlacementPosition(MergedMapSprite placement) =>
        _bodies.TryGetValue(placement, out var body) ? body.Position : _statics.TryGetValue(placement, out var stand) ? stand.Position : placement.Position;

    private Sprite? AddStatic(MergedMapSprite placement, SpriteDefinition definition)
    {
        if (definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man or SpriteClass.Cannon)
            return null;

        var stand = ConstructSprite(definition, placement.Position, placement.Direction, placement.Army, 0, UpdateRole.Sprite);
        stand.Placement = placement;
        _statics[placement] = stand;

        if (definition.Object.SpriteClass == SpriteClass.BuiltTerrain)
        {
            ConstructBuiltTerrain(stand);
        }

        return stand;
    }

    [EngineRoutine(0x0040edf0)]
    internal void ConstructBuiltTerrain(Sprite sprite)
    {
        if (!_hasHardwareGround)
        {
            CreateEmptyHardwareGround();
        }

        if (IsBaked(sprite.Definition))
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
    }

    internal static bool IsBaked(SpriteDefinition definition) => definition.Object.SpriteClass == SpriteClass.BuiltTerrain && !SpriteCollector.IsHashed(definition);

    [EngineRoutine(0x00412a40)]
    private void CreateEmptyHardwareGround()
    {
        _hasHardwareGround = true;
        var definition = Definitions.CreateHardwareGround();
        ConstructSprite(definition, new Vector3(Movement.MapSize.ToVector2() * HalfShare, 0), default, definition.Army, 0, UpdateRole.Sprite);
        Session.Logger.Write("Create Empty Hardware Ground"u8);
    }

    [EngineRoutine(0x0043f500)]
    private void RemoveStatic(Sprite sprite)
    {
        if (sprite.Placement is not null)
        {
            _statics.Remove(sprite.Placement);
        }

        _builders.Remove(sprite);
        Collector.DeleteFromHash(sprite);
        Effects.Detach(sprite);
        ReleaseSprite(sprite);
    }

    private Sprite AddScripted(SpriteDefinition definition, Vector3 position, Angle direction, Army? army = null, MergedMapSprite? placement = null)
    {
        var sprite = ConstructSprite(definition, position, direction, army ?? Definitions.GetArmy(definition), 0, UpdateRole.Scripted);
        sprite.Placement = placement;
        _scripted.Add(sprite);

        return sprite;
    }

    private bool TickScripted(Sprite sprite)
    {
        if ((sprite.Owner is null || sprite.Owner.Link == sprite) && Tact(sprite))
            return true;

        RemoveScripted(sprite);
        UnlinkDestroyed(sprite);

        return false;
    }

    private void RemoveScripted(Sprite sprite)
    {
        _scripted.Remove(sprite);
        Collector.DeleteFromHash(sprite);
        Effects.Detach(sprite);

        if (sprite.Placement is not null)
        {
            RemoveSprite(sprite.Placement);
        }

        ReleaseSprite(sprite);
    }

    /// <summary>
    /// Runs the creation handler of a sprite's definition.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public void RunCreationHook(Sprite sprite) => RunCreationHandler(sprite);

    private void Destroy(MergedMapSprite placement)
    {
        if (_bodies.Remove(placement, out var body))
        {
            body.Hp = 0;
            _updates.Remove(body.Definition.Layer, body);
            Collector.DeleteFromHash(body);
            Effects.Detach(body);
            ReleaseSprite(body);
        }

        for (int i = _scripted.Count - 1; i >= 0; i--)
        {
            var sprite = _scripted[i];

            if (ReferenceEquals(sprite.Placement, placement))
            {
                _updates.Remove(sprite.Definition.Layer, sprite);
                _scripted.RemoveAt(i);
                Collector.DeleteFromHash(sprite);
                Effects.Detach(sprite);
                ReleaseSprite(sprite);
            }
        }

        RemoveSprite(placement);
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

    private void RemoveSprite(MergedMapSprite placement)
    {
        if (_removed.Contains(placement))
            return;

        RemovePlacement(placement);
    }

    private MergedMapSprite? FindSprite(KnownObject id, Vector2 point)
    {
        MergedMapSprite? found = null;
        float foundX = float.MaxValue;
        float foundY = float.MaxValue;
        foreach (var placement in _sprites)
        {
            var definition = Definitions.Find(id);

            if (placement.Id != id || _removed.Contains(placement) || definition is null)
                continue;

            if (definition.GetCollisionVolume(GetPlacementPosition(placement)).Covers(point) && (found is null || definition.Object.Size.X < foundX || definition.Object.Size.Y < foundY))
            {
                found = placement;
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
    private void UpdateScreen(bool drawFrame)
    {
        if (_environment.HasFlag(MapEnvironment.Earthquake) && !IsPaused)
        {
            var screen = Session.ScreenSize;
            double centerX = screen.Width * HalfShare + (double)Camera.X;
            double centerY = screen.Height * HalfShare + (double)Camera.Y;
            int y = _rng.Next() % (ShakeReach * 2 + 1);
            int x = _rng.Next() % (ShakeReach * 2 + 1);
            var offset = new Vector2(ShakeReach - x, ShakeReach - y);
            SetPosition(new Vector2((float)(centerX + offset.X), (float)(centerY + offset.Y)), CameraMove.None);
            CameraShake = new CameraShake(Time, offset);
            SetPosition(new Vector2((float)centerX, (float)centerY), CameraMove.None);
        }
        else
        {
            CameraShake = null;
        }

        UpdateEffects(drawFrame);
    }

    /// <summary>
    /// Gets the camera shake of the frame.
    /// </summary>
    public CameraShake? CameraShake { get; private set; }

    /// <summary>
    /// Gets the screen flash in progress.
    /// </summary>
    public ScreenFlash? ScreenFlash { get; private set; }

    /// <summary>
    /// Gets the gamma the frame is drawn with, the channel reductions and additions applied to the whole screen.
    /// </summary>
    public Gamma Gamma { get; private set; }
}

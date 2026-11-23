using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

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

    private readonly Dictionary<MergedMapSprite, Sprite> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MergedMapSprite> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MergedMapSprite, Sprite> _statics = new(ReferenceEqualityComparer.Instance);
    private List<MergedMapSprite> _sprites;
    private SpriteDefinition? _hardwareGround;

    /// <summary>
    /// Gets the definition of the hardware ground, or the empty definition if there isn't one.
    /// </summary>
    public SpriteDefinition HardwareGroundDefinition => _hardwareGround ?? Definitions.Empty;

    /// <summary>
    /// Gets a value indicating whether the map has a hardware ground.
    /// </summary>
    public bool HasHardwareGround => HardwareGroundDefinition != Definitions.Empty;

    /// <summary>
    /// Gets a value indicating whether the level script declared the mission complete.
    /// </summary>
    public bool HasWon => ReadScriptNumber(GameStateVariable) == GameWon;

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
    /// Gets the gamma the frame is drawn with, meaning the channel reductions and additions applied to the whole screen.
    /// </summary>
    public Gamma Gamma => Session.Graphics.Gamma;

    /// <summary>
    /// Determines whether a sprite was removed from the map.
    /// </summary>
    /// <param name="placement">The sprite.</param>
    /// <returns><see langword="true"/> when the sprite is gone; otherwise, <see langword="false"/>.</returns>
    public bool IsRemoved(MergedMapSprite placement) => _removed.Contains(placement);

    /// <summary>
    /// Returns the live sprite created from a placed map sprite.
    /// </summary>
    /// <param name="placement">The placed map sprite.</param>
    /// <returns>The sprite, or null if there isn't one.</returns>
    public Sprite? FindPlacementSprite(MergedMapSprite placement) =>
        _bodies.TryGetValue(placement, out var body) ? body : _statics.GetValueOrDefault(placement) ?? FindScripted(placement);

    internal static bool IsBaked(SpriteDefinition definition) => definition.Object.SpriteClass == SpriteClass.BuiltTerrain && !SpriteCollector.IsHashed(definition);

    [EngineRoutine(0x0040edf0)]
    internal void ConstructBuiltTerrain(Sprite sprite)
    {
        if (!HasHardwareGround)
        {
            CreateEmptyHardwareGround();
        }

        HardwareGroundDefinition.AddVidToTextures(this, sprite);

        if (IsBaked(sprite.Definition))
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }
    }

    private Vector3 GetPlacementPosition(MergedMapSprite placement) =>
        _bodies.TryGetValue(placement, out var body) ? body.Position : _statics.TryGetValue(placement, out var stand) ? stand.Position : placement.Position;

    private Sprite? AddStatic(MergedMapSprite placement, SpriteDefinition definition)
    {
        if (definition.Object.SpriteClass is SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man or SpriteClass.Cannon)
            return null;

        var stand = ConstructSpriteOfClass(definition, placement.Position, placement.Direction, Definitions.GetArmy(definition), 0);
        ChangeArmy(stand, (int)placement.Army);
        stand.Placement = placement;
        _statics[placement] = stand;

        if (definition.Object.SpriteClass == SpriteClass.BuiltTerrain)
        {
            ConstructBuiltTerrain(stand);
        }

        return stand;
    }

    private Sprite? FindScripted(MergedMapSprite placement)
    {
        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = 0; i < list.Count; i++)
            {
                var sprite = list[i];

                if (sprite is not null && sprite.Definition.Id.IsScripted && sprite.Placement == placement)
                    return sprite;
            }
        }

        return null;
    }

    [EngineRoutine(0x00412a40)]
    private void CreateEmptyHardwareGround()
    {
        var definition = new HardwareSpriteDefinition(SpriteCatalog.HardwareGroundId, System.Drawing.Size.Truncate(Movement.MapSize));
        Definitions.SetDefinition(SpriteCatalog.HardwareGroundId, definition);
        _hardwareGround = definition;
        ConstructSpriteOfClass(definition, new Vector3(Movement.MapSize.ToVector2() * HalfShare, 0), default, definition.Army, 0);
        Session.Logger.Write("Create Empty Hardware Ground"u8);
    }

    private Sprite AddScripted(SpriteDefinition definition, Vector3 position, Angle direction, Army? army = null, MergedMapSprite? placement = null)
    {
        var sprite = ConstructSpriteOfClass(definition, position, direction, Definitions.GetArmy(definition), 0);

        if (army is not null)
        {
            ChangeArmy(sprite, (int)army.Value);
        }

        sprite.Placement = placement;

        return sprite;
    }

    private void Destroy(MergedMapSprite placement)
    {
        if (_bodies.TryGetValue(placement, out var body))
        {
            body.Hp = 0;
            DeleteSprite(body);
        }

        for (int layer = 0; layer < LayerCount; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = 0; i < list.Count; i++)
            {
                var sprite = list[i];

                if (sprite is not null && sprite.Definition.Id.IsScripted && ReferenceEquals(sprite.Placement, placement))
                {
                    DeleteSprite(sprite);
                }
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
        var definition = Definitions.Find(id);
        if (definition is null)
            return null;

        foreach (var placement in _sprites)
        {
            if (placement.Id != id || _removed.Contains(placement))
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
}

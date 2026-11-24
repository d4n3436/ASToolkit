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
    /// Gets the gamma applied to the whole screen.
    /// </summary>
    public Gamma Gamma => Session.Graphics.Gamma;

    internal static bool IsBaked(SpriteDefinition definition) => definition.Class == SpriteClass.BuiltTerrain && !SpriteCollector.IsHashed(definition);

    [EngineRoutine(0x0040edf0)]
    private Sprite ConstructBuiltTerrain(BuiltTerrain sprite, Angle direction, Sprite? parent)
    {
        ConstructSprite(sprite, direction, parent);

        if (!HasHardwareGround)
        {
            CreateEmptyHardwareGround();
        }

        HardwareGroundDefinition.AddVidToTextures(this, sprite);

        if (IsBaked(sprite.Definition))
        {
            sprite.ChangeAnimation(SpriteAnimation.Death);
        }

        return sprite;
    }

    [EngineRoutine(0x00412a40)]
    private void CreateEmptyHardwareGround()
    {
        var definition = new HardwareSpriteDefinition(SpriteCatalog.HardwareGroundId, System.Drawing.Size.Truncate(Size), Definitions.FirstWeapon, Session.Logger);
        Definitions.SetDefinition(SpriteCatalog.HardwareGroundId, definition);
        _hardwareGround = definition;
        AddSprite(definition, new Vector3(Size.ToVector2() * HalfShare, 0), default);
        Session.Logger.Write("Create Empty Hardware Ground"u8);
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
}

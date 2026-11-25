using System.Numerics;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
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

    /// <summary>
    /// Gets the shared data and frames of the hardware ground, or shared data without frames if there isn't a hardware ground.
    /// </summary>
    public Vid HardwareGroundVid => _vids[(int)HardwareGroundId] ?? EmptyVid;

    /// <summary>
    /// Gets a value indicating whether the map has a hardware ground.
    /// </summary>
    public bool HasHardwareGround => HardwareGroundVid != EmptyVid;

    /// <summary>
    /// Gets a value indicating whether the level script declared the mission complete.
    /// </summary>
    public bool HasWon => ReadScriptNumber("GameState"u8) == GameWon;

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

    internal static bool IsBaked(Vid vid) => vid.Class == SpriteClass.BuiltTerrain && !SpriteCollector.IsHashed(vid);

    [EngineRoutine(0x00412a40)]
    internal void CreateEmptyHardwareGround()
    {
        if (_vidCount < (int)HardwareGroundId + 1)
        {
            _vidCount = (int)HardwareGroundId + 1;
        }

        _vids[(int)HardwareGroundId]?.Destruct(Session.Error);
        var vid = new VidHardware(HardwareGroundId, System.Drawing.Size.Truncate(Size), _weapons!, Session.Error);
        _vids[(int)HardwareGroundId] = vid;
        CreateSprite(vid, new Vector3(Size.ToVector2() * HalfShare, 0), default);
        Session.Error.Write("Create Empty Hardware Ground"u8);
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

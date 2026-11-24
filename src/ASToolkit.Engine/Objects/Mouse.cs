using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the mouse cursor sprite.
/// </summary>
public sealed class Mouse : Sprite
{
    private const string CursorFolder = "cursores\\";
    private const string CursorExtension = ".ani";
    private const int LastAnimation = 0x10;
    private const int NoCursor = -1;

    private static readonly string[] CursorNames =
    [
        "arrow", "noammo", "move", "clash", "repair", "attack", "farattack", "select", "nomove", "cycle", "link", "unlink",
        "cantmove", "patrol", "delete", "capture", "mine", "unmine", "small-arrow", "small-noammo", "small-move", "small-taran",
        "small-repair", "small-attack", "small-farattack", "small-select", "small-nomove", "cycle", "small-link", "small-unlink",
        "small-cantmove", "small-patrol", "delete", "small-capture", "small-mine", "small-unmine"
    ];

    private readonly CursorImage[] _cursors = new CursorImage[CursorNames.Length];

    /// <summary>
    /// Initializes a new instance of the <see cref="Mouse"/> class on a map, kept out of the map's update lists, with the system cursor in use and no cursor loaded.
    /// </summary>
    /// <param name="vid">The shared data and frames of the cursor's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the cursor takes its army and height from, or null.</param>
    /// <param name="map">The map the cursor belongs to.</param>
    [EngineRoutine(0x00446c90)]
    public Mouse(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        if (Vid != Map.Vids.Empty)
        {
            Map.RemoveFromUpdates(this);
        }
        else if (Link is not null)
        {
            Map.RemoveFromUpdates(Link);
        }

        if (Vid == Map.Vids.Empty)
        {
            AddRef();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the cursor is a system cursor.
    /// </summary>
    public bool IsHardwareCursor { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the cursor is loaded and showing.
    /// </summary>
    public bool IsCursorLoaded { get; private set; }

    /// <summary>
    /// Gets the animation of the system cursor that is showing, or -1 if no system cursor is set.
    /// </summary>
    public int ShownCursor { get; private set; } = NoCursor;

    /// <summary>
    /// Gets the system cursor that is showing, or an invalid image when no system cursor is set or its file is missing.
    /// </summary>
    public CursorImage ShownCursorImage => ShownCursor >= 0 && ShownCursor < _cursors.Length ? _cursors[ShownCursor] : default;

    /// <summary>
    /// Draws the cursor if it's loaded and isn't a system cursor.
    /// </summary>
    /// <param name="screen">The screen to draw to.</param>
    /// <param name="map">The map.</param>
    /// <param name="camera">The top left corner of the view, in map pixels.</param>
    [EngineRoutine(0x00446d20)]
    public override void Draw(Screen screen, Map map, Vector2 camera)
    {
        if (!IsHardwareCursor && IsCursorLoaded)
        {
            base.Draw(screen, map, camera);
        }
    }

    /// <summary>
    /// Loads the cursor and, if it's a system cursor, the system cursor of each animation.
    /// </summary>
    /// <param name="fileSystem">The file system that holds the cursor files.</param>
    [EngineRoutine(0x00446d90)]
    public void Enable(IGameFileSystem fileSystem)
    {
        if (IsCursorLoaded)
            return;

        IsCursorLoaded = true;
        ShownCursor = NoCursor;
        if (!IsHardwareCursor)
            return;

        for (int i = 0; i < CursorNames.Length; i++)
        {
            string path = CursorFolder + CursorNames[i] + CursorExtension;
            _cursors[i] = fileSystem.TryRead(path, out var data) ? new CursorImage(data) : default;
        }

        ShownCursor = (int)Animation;
    }

    /// <summary>
    /// Hides the cursor and releases the system cursors.
    /// </summary>
    [EngineRoutine(0x00446ef0)]
    public void Disable()
    {
        if (!IsCursorLoaded)
            return;

        IsCursorLoaded = false;
        if (!IsHardwareCursor)
            return;

        ShownCursor = NoCursor;
        Array.Clear(_cursors);
    }

    /// <summary>
    /// Switches to the system cursor.
    /// </summary>
    /// <param name="empty">The shared data, without frames, that the sprite takes.</param>
    /// <param name="fileSystem">The file system that holds the cursor files.</param>
    [EngineRoutine(0x00446f40)]
    public void HardwareOn(Vid empty, IGameFileSystem fileSystem)
    {
        if (IsHardwareCursor)
            return;

        Disable();
        ReplaceVid(empty);
        IsHardwareCursor = true;
        Enable(fileSystem);
    }

    /// <summary>
    /// Switches to drawing the cursor as this sprite.
    /// </summary>
    /// <param name="vid">The shared data and frames of the software cursor's object ID.</param>
    /// <param name="fileSystem">The file system that holds the cursor files.</param>
    [EngineRoutine(0x00446f70)]
    public void HardwareOff(Vid vid, IGameFileSystem fileSystem)
    {
        if (!IsHardwareCursor)
            return;

        Disable();
        var state = Animation;
        ReplaceVid(vid);
        base.ChangeAnimation(state == SpriteAnimation.Stand ? SpriteAnimation.StopMove : SpriteAnimation.Stand);
        base.ChangeAnimation(state);
        IsHardwareCursor = false;
        Enable(fileSystem);
    }

    /// <summary>
    /// Changes the cursor to an animation, showing the animation's system cursor if the cursor is a system cursor.
    /// </summary>
    /// <param name="type">The animation of the cursor.</param>
    [EngineRoutine(0x00446fd0)]
    public new void ChangeAnimation(SpriteAnimation type)
    {
        if (!IsHardwareCursor)
        {
            base.ChangeAnimation(type);
            return;
        }

        if (Animation != type && IsCursorLoaded)
        {
            ShownCursor = (int)type;
        }

        if ((int)type <= LastAnimation)
        {
            base.ChangeAnimation(type);
            return;
        }

        SetAnimationValue(type);
    }

    [EngineRoutine(0x00447030)]
    internal override int Action(ActionCode act, int var1, int var2, int var3, Sprite? target)
    {
        switch (act)
        {
            case ActionCode.ChangeAnimation:
                Map.CursorSprite.ChangeAnimation((SpriteAnimation)var1);
                return 0;
            case ActionCode.ChangeVid:
                if (Map.Vids.Find((KnownObject)var1) is null || (KnownObject)var1 == Vid.Id)
                    return 0;

                Map.AddToUpdates(this);
                base.Action(act, var1, var2, var3, target);
                Map.Collector.DeleteFromHash(this);
                foreach (var link in EnumerateLinks())
                {
                    Map.Collector.DeleteFromHash(link);
                }

                Map.RemoveFromUpdates(this);

                return 0;
            case ActionCode.ChangeCoor:
                MoveSystemCursor(new Point(var1, var2));
                return 0;
            default:
                return base.Action(act, var1, var2, var3, target);
        }
    }

    private void MoveSystemCursor(Point target)
    {
        var screen = target + new Size(Map.Session.Graphics.WindowPosition);
        Map.Input.PostMessage(WindowMessageId.HitTest, 0, (screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF));
    }
}

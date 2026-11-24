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
    /// Initializes a new instance of the <see cref="Mouse"/> class, with the system cursor in use and no cursor loaded.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="definition">The definition of the sprite.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Mouse(int number, SpriteDefinition definition, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, definition, position, army, hitPoints, map, direction)
    {
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
    public void LoadCursor(IGameFileSystem fileSystem)
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
    public void TurnCursorOff()
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
    /// <param name="empty">The empty definition the sprite takes.</param>
    /// <param name="fileSystem">The file system that holds the cursor files.</param>
    [EngineRoutine(0x00446f40)]
    public void UseHardwareCursor(SpriteDefinition empty, IGameFileSystem fileSystem)
    {
        if (IsHardwareCursor)
            return;

        TurnCursorOff();
        ReplaceDefinition(empty);
        IsHardwareCursor = true;
        LoadCursor(fileSystem);
    }

    /// <summary>
    /// Switches to drawing the cursor as this sprite.
    /// </summary>
    /// <param name="definition">The definition of the software cursor.</param>
    /// <param name="fileSystem">The file system that holds the cursor files.</param>
    [EngineRoutine(0x00446f70)]
    public void UseSoftwareCursor(SpriteDefinition definition, IGameFileSystem fileSystem)
    {
        if (!IsHardwareCursor)
            return;

        TurnCursorOff();
        var state = Animation;
        ReplaceDefinition(definition);
        ChangeAnimation(state == SpriteAnimation.Stand ? SpriteAnimation.StopMove : SpriteAnimation.Stand);
        ChangeAnimation(state);
        IsHardwareCursor = false;
        LoadCursor(fileSystem);
    }

    /// <summary>
    /// Changes the cursor to an animation, showing the animation's system cursor if the cursor is a system cursor.
    /// </summary>
    /// <param name="type">The animation of the cursor.</param>
    [EngineRoutine(0x00446fd0)]
    public void ShowCursor(SpriteAnimation type)
    {
        if (!IsHardwareCursor)
        {
            ChangeAnimation(type);
            return;
        }

        if (Animation != type && IsCursorLoaded)
        {
            ShownCursor = (int)type;
        }

        if ((int)type <= LastAnimation)
        {
            ChangeAnimation(type);
            return;
        }

        SetAnimationValue(type);
    }

    [EngineRoutine(0x00447030)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.ChangeAnimation:
                Map.CursorSprite.ShowCursor((SpriteAnimation)var1);
                return 0;
            case ActionCode.ChangeVid:
                if (Map.Definitions.Find((KnownObject)var1) is null || (KnownObject)var1 == Definition.Id)
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
